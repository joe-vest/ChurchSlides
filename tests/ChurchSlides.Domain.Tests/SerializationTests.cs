using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Tests;

public class SerializationTests
{
    // ---- 1. Round trip, every entity -------------------------------------------------

    private static void AssertRoundTrips<T>(T sample) where T : class
    {
        var json = VersionedFile.Serialize(sample);
        var loaded = VersionedFile.Deserialize<T>(json);

        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(1, loaded.FileVersion);
        Assert.NotNull(loaded.Value);
        Assert.Equal(json, VersionedFile.Serialize(loaded.Value!));
    }

    [Fact] public void Show_round_trips() => AssertRoundTrips(SampleData.Song());
    [Fact] public void Service_round_trips() => AssertRoundTrips(SampleData.Service());
    [Fact] public void Template_round_trips() => AssertRoundTrips(SampleData.Template());
    [Fact] public void ServiceTemplate_round_trips() => AssertRoundTrips(SampleData.ServiceTemplate());
    [Fact] public void CategoryList_round_trips() => AssertRoundTrips(SampleData.Categories());
    [Fact] public void LibrarySettings_round_trips() => AssertRoundTrips(SampleData.Settings());

    [Fact]
    public void Show_fields_survive_a_round_trip()
    {
        var show = VersionedFile.Deserialize<Show>(VersionedFile.Serialize(SampleData.Song())).Value!;

        Assert.Equal("Amazing Grace — Señor", show.Title);
        Assert.Equal(ShowKind.Song, show.Kind);
        Assert.Equal(CopyrightPlacement.FirstSlide, show.CopyrightPlacement);
        Assert.Equal(3, show.Slides.Length);
        Assert.Equal("Chorus", show.Slides[1].Label);

        var detached = show.Slides[1].TextBoxes[0];
        Assert.True(detached.IsDetached);
        Assert.Equal("body", detached.TemplateBoxName); // TPL-023: remembered so Reattach works
        Assert.Equal(new RectRelative(0.1, 0.2, 0.8, 0.5), detached.Geometry);
        Assert.False(show.Slides[0].TextBoxes[0].IsDetached);
        Assert.False(show.Slides[0].SkippedByDefault);
        Assert.True(show.Slides[2].SkippedByDefault); // SHOW-008
        Assert.Equal(HorizontalAlign.Left, detached.HAlign);
        Assert.Equal(1.2, detached.LineSpacing);

        // SLD-021: formatting on character ranges survives.
        var run = (TextRun)show.Slides[0].TextBoxes[0].Content.Paragraphs[0].Inlines[1];
        Assert.Equal("sweet", run.Text);
        Assert.True(run.Format!.Bold);
        Assert.Null(((TextRun)show.Slides[0].TextBoxes[0].Content.Paragraphs[0].Inlines[0]).Format); // unformatted text carries no format

        // AUD-005: absolute cue times, trim stored separately.
        Assert.Equal(TimeSpan.FromSeconds(2.4), show.Audio!.TrimStart);
        Assert.Equal(TimeSpan.FromSeconds(31.5), show.Audio.Cues[0].Position);
        Assert.Equal(SampleData.Chorus, show.Audio.Cues[0].SlideId);
    }

    // ---- 2. Polymorphism -------------------------------------------------------------

    [Fact]
    public void Service_item_kinds_keep_their_concrete_types()
    {
        var service = VersionedFile.Deserialize<Service>(VersionedFile.Serialize(SampleData.Service())).Value!;

        Assert.Collection(service.Items,
            i => Assert.IsType<ShowReferenceItem>(i),
            i => Assert.IsType<ShowCopyItem>(i),
            i => Assert.IsType<PlaceholderItem>(i),
            i => Assert.IsType<ScriptureItem>(i));

        var reference = (ShowReferenceItem)service.Items[0];
        Assert.Equal(SampleData.Chorus, Assert.Single(reference.SkippedSlideIds));
        Assert.Equal("Opening song", reference.FilledFrom!.Label);

        var copy = (ShowCopyItem)service.Items[1];
        Assert.Equal(SampleData.I<Show>(5), copy.OriginShowId);
        Assert.Single(copy.LocalShow.Slides);
    }

    [Fact]
    public void Actions_keep_their_trigger_and_command_types()
    {
        var service = VersionedFile.Deserialize<Service>(VersionedFile.Serialize(SampleData.Service())).Value!;

        Assert.IsType<ServiceStarted>(service.Actions[0].Trigger);
        Assert.Equal(14, service.Actions[0].Commands.Length);

        var itemTarget = Assert.IsType<GoToSlide>(service.Actions[0].Commands[4]).Target;
        Assert.Equal(SampleData.I<ServiceItem>(100), itemTarget.ItemId);
        var showTarget = Assert.IsType<GoToSlide>(service.Actions[0].Commands[5]).Target;
        Assert.Null(showTarget.ItemId); // "within the current Show"

        var expired = Assert.IsType<TimerExpired>(service.Actions[1].Trigger);
        Assert.Equal("ServiceStart", expired.TimerName);
        Assert.Equal(TimeSpan.FromMilliseconds(500), service.Actions[1].Delay);
    }

    [Fact]
    public void Tokens_keep_their_concrete_types()
    {
        var show = VersionedFile.Deserialize<Show>(VersionedFile.Serialize(SampleData.Song())).Value!;
        var paragraphs = show.Slides[2].TextBoxes[0].Content.Paragraphs;

        Assert.IsType<VariableToken>(((TokenRun)paragraphs[0].Inlines[1]).Token);
        Assert.IsType<TimerToken>(((TokenRun)paragraphs[1].Inlines[0]).Token);
        Assert.IsType<ShowMetadataToken>(((TokenRun)paragraphs[2].Inlines[0]).Token);
        Assert.IsType<ChurchLicenseToken>(((TokenRun)paragraphs[2].Inlines[2]).Token);
    }

    [Fact]
    public void Discriminator_may_appear_anywhere_in_hand_edited_json()
    {
        // Move "$type" to the end of every item object, as another tool or a person might write it.
        var root = JsonNode.Parse(VersionedFile.Serialize(SampleData.Service()))!.AsObject();
        foreach (var item in root["data"]!["items"]!.AsArray())
        {
            var obj = item!.AsObject();
            var kind = obj["$type"]!.GetValue<string>();
            obj.Remove("$type");
            obj.Add("$type", kind);
        }

        var result = VersionedFile.Deserialize<Service>(root.ToJsonString());

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.IsType<ShowReferenceItem>(result.Value!.Items[0]);
        Assert.IsType<ScriptureItem>(result.Value.Items[3]);
    }

    [Fact]
    public void Every_derived_type_is_registered_and_appears_in_the_sample_files()
    {
        // Adding a new ServiceItem/Trigger/Command/Inline/Token kind without registering it and
        // adding it to SampleData fails here.
        var allSamples = string.Concat(
            VersionedFile.Serialize(SampleData.Song()),
            VersionedFile.Serialize(SampleData.Service()),
            VersionedFile.Serialize(SampleData.ServiceTemplate()));

        foreach (var baseType in new[] { typeof(ServiceItem), typeof(ActionTrigger), typeof(Command), typeof(Inline), typeof(Token) })
        {
            var registered = baseType.GetCustomAttributes<JsonDerivedTypeAttribute>().ToArray();
            var concrete = typeof(Show).Assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract).ToArray();

            Assert.Equal(concrete.Length, registered.Length);
            foreach (var attr in registered)
                Assert.Contains($"\"$type\": \"{attr.TypeDiscriminator}\"", allSamples);
        }
    }

    // ---- 3. Forward compatibility and bad input (LIB-004) ------------------------------

    private static string WithVersion(string json, int version, Action<JsonObject>? edit = null)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root["schemaVersion"] = version;
        edit?.Invoke(root["data"]!.AsObject());
        return root.ToJsonString();
    }

    [Fact]
    public void Newer_file_with_unknown_fields_opens_read_only()
    {
        var json = WithVersion(VersionedFile.Serialize(SampleData.Song()), 2, data => data["futureField"] = "hello");

        var result = VersionedFile.Deserialize<Show>(json);

        Assert.Equal(LoadStatus.ReadOnlyNewer, result.Status);
        Assert.True(result.IsReadOnly);
        Assert.Equal(2, result.FileVersion);
        Assert.Equal("Amazing Grace — Señor", result.Value!.Title);
    }

    [Fact]
    public void Newer_file_with_an_unknown_kind_is_unreadable_with_a_clear_message()
    {
        var json = WithVersion(VersionedFile.Serialize(SampleData.Service()), 2)
            .Replace("\"showReference\"", "\"hologram\"");

        var result = VersionedFile.Deserialize<Service>(json);

        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("newer version", result.Error);
    }

    [Fact]
    public void Unknown_kind_at_the_current_version_is_unreadable_not_an_exception()
    {
        var json = VersionedFile.Serialize(SampleData.Service()).Replace("\"showReference\"", "\"hologram\"");
        var result = VersionedFile.Deserialize<Service>(json);
        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Older_file_is_flagged_for_migration()
    {
        // No version 2 exists yet, so pretend the app is at version 2 and the file is at 1.
        var bytes = System.Text.Encoding.UTF8.GetBytes(VersionedFile.Serialize(SampleData.Template()));
        var result = VersionedFile.Deserialize<Template>(bytes, current: 2);
        Assert.Equal(LoadStatus.NeedsMigration, result.Status);
        Assert.Equal(1, result.FileVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "schemaVersion": 0, "data": {} }""")]
    [InlineData("""{ "schemaVersion": 1 }""")]
    [InlineData("""{ "schemaVersion": 1, "data": { "name": "no id" } }""")]
    public void Damaged_files_are_unreadable_and_never_throw(string json)
    {
        var result = VersionedFile.Deserialize<Template>(json);
        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.False(result.HasValue);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Absolute_media_paths_in_a_file_are_rejected_on_load()
    {
        // LIB-003: even a hand-edited file can't smuggle in an absolute path.
        var json = VersionedFile.Serialize(SampleData.Template())
            .Replace("media/images/hymn-bg.png", @"C:\\Users\\me\\bg.png");
        var result = VersionedFile.Deserialize<Template>(json);
        Assert.Equal(LoadStatus.Unreadable, result.Status);
    }

    // ---- 4. Missing fields -----------------------------------------------------------

    [Theory]
    [InlineData("slides")] // a collection
    [InlineData("title")]
    [InlineData("copyrightPlacement")]
    public void A_file_missing_a_non_optional_field_is_unreadable_and_names_the_field(string field)
    {
        // Strict policy: init-only properties would otherwise silently become default(ImmutableArray) or null.
        var root = JsonNode.Parse(VersionedFile.Serialize(SampleData.Song()))!.AsObject();
        root["data"]!.AsObject().Remove(field);

        var result = VersionedFile.Deserialize<Show>(root.ToJsonString());

        Assert.Equal(LoadStatus.Unreadable, result.Status);
        Assert.Contains(field, result.Error);
    }

    [Fact]
    public void Optional_fields_may_be_absent()
    {
        // Audio, Metadata, DefaultTemplateId and friends are nullable and omitted when empty.
        var json = VersionedFile.Serialize(SampleData.SmallPresentation());
        var show = VersionedFile.Deserialize<Show>(json).Value!;

        Assert.Null(show.Audio);
        Assert.Null(show.Metadata);
        Assert.Null(show.DefaultTemplateId);
        Assert.False(show.Tags.IsDefault);
        Assert.Empty(show.Tags);
    }

    [Fact]
    public void Null_in_a_non_nullable_field_is_unreadable()
    {
        var json = VersionedFile.Serialize(SampleData.Template()).Replace("\"name\": \"Hymns\"", "\"name\": null");
        Assert.Equal(LoadStatus.Unreadable, VersionedFile.Deserialize<Template>(json).Status);
    }

    // ---- Format properties -----------------------------------------------------------

    [Fact]
    public void Saved_text_is_readable_and_stable()
    {
        var json = VersionedFile.Serialize(SampleData.Song());

        Assert.DoesNotContain('\r', json); // same bytes on every machine
        Assert.EndsWith("\n", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"kind\": \"Song\"", json); // enums as strings
        Assert.Contains("— Señor", json); // accents and symbols not escaped
        Assert.Contains("\"00000000-0000-0000-0000-000000000001\"", json); // ids as plain GUID strings
        Assert.DoesNotContain("null", json); // nulls omitted
        Assert.DoesNotContain("isDetached", json); // derived, not stored
    }

    [Fact]
    public void Show_without_audio_omits_the_audio_property()
    {
        var json = VersionedFile.Serialize(SampleData.SmallPresentation());
        Assert.DoesNotContain("\"audio\"", json);
    }

    [Fact]
    public void Serializing_an_unregistered_type_is_refused()
    {
        Assert.Throws<ArgumentException>(() => VersionedFile.Serialize(new Cue(SampleData.Chorus, TimeSpan.Zero)));
    }

    // ---- 7. Golden files ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Saved_format_matches_the_reviewed_golden_file(string fileName, Func<string> produce, Func<string, LoadStatus> load)
    {
        var actual = produce();
        GoldenFile.Check(fileName, actual);

        // And the committed file itself loads.
        Assert.Equal(LoadStatus.Ok, load(GoldenFile.Read(fileName)));
    }

    public static TheoryData<string, Func<string>, Func<string, LoadStatus>> GoldenCases() => new()
    {
        { "song-with-audio.show.json", () => VersionedFile.Serialize(SampleData.Song()), s => VersionedFile.Deserialize<Show>(s).Status },
        { "service-all-item-kinds.service.json", () => VersionedFile.Serialize(SampleData.Service()), s => VersionedFile.Deserialize<Service>(s).Status },
        { "hymns.template.json", () => VersionedFile.Serialize(SampleData.Template()), s => VersionedFile.Deserialize<Template>(s).Status },
        { "sunday-morning.service-template.json", () => VersionedFile.Serialize(SampleData.ServiceTemplate()), s => VersionedFile.Deserialize<ServiceTemplate>(s).Status },
        { "categories.json", () => VersionedFile.Serialize(SampleData.Categories()), s => VersionedFile.Deserialize<CategoryList>(s).Status },
        { "library.json", () => VersionedFile.Serialize(SampleData.Settings()), s => VersionedFile.Deserialize<LibrarySettings>(s).Status },
    };
}
