using System.Text.Json.Serialization;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Serialization;

/// <summary>
/// The only serializer metadata for Library files (source generated, no reflection).
/// Every file kind that can be saved has an Envelope registration here.
/// Enums are written as strings (for example "Song") so saved files stay readable.
/// Strict policy: every non-optional property must be present in a file. The app always writes
/// complete files, and a file missing one is reported as unreadable instead of silently getting
/// defaults (init-only properties would otherwise be set to CLR defaults, not their initializers).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EnvelopeHeader))]
[JsonSerializable(typeof(Envelope<Show>))]
[JsonSerializable(typeof(Envelope<Service>))]
[JsonSerializable(typeof(Envelope<Template>))]
[JsonSerializable(typeof(Envelope<ServiceTemplate>))]
[JsonSerializable(typeof(Envelope<CategoryList>))]
[JsonSerializable(typeof(Envelope<LibrarySettings>))]
internal sealed partial class ChurchSlidesJsonContext : JsonSerializerContext;
