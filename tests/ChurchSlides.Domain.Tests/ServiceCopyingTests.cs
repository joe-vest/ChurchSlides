using ChurchSlides.Domain.Editing;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class ServiceCopyingTests
{
    private static readonly DateOnly Date = new(2027, 1, 3);

    private static ServiceTemplate TemplateWithItemAction()
    {
        var template = SampleData.ServiceTemplate();
        var first = template.Items[0] with
        {
            Actions = [new TriggeredAction { Trigger = new ShowEnded(), Commands = [new GoToItem { ItemId = I<ServiceItem>(202) }] }],
        };
        return template with { Items = template.Items.SetItem(0, first) };
    }

    // ---- From a Service Template (SVC-030, SVC-035, VAR-004, AS-04) ------------------------

    [Fact]
    public void SVC_030_a_new_service_gets_new_item_ids_and_the_templates_content()
    {
        var template = SampleData.ServiceTemplate();
        var service = ServiceCopying.FromTemplate(template, "Sunday 3 Jan", Date);

        Assert.NotEqual(Id<Service>.New(), service.Id);
        Assert.False(service.Id.IsEmpty);
        Assert.Equal("Sunday 3 Jan", service.Name);
        Assert.Equal(Date, service.Date);
        Assert.Equal(ServiceMode.Rehearse, service.Mode);
        Assert.Equal(template.Id, service.CreatedFromServiceTemplateId);

        Assert.Equal(template.Items.Length, service.Items.Length);
        Assert.Empty(template.Items.Select(i => i.Id).Intersect(service.Items.Select(i => i.Id)));
        Assert.Equal(template.Items.Select(i => i.GetType()), service.Items.Select(i => i.GetType()));
        Assert.Equal("Opening song", ((PlaceholderItem)service.Items[0]).Placeholder.Label);
        Assert.Equal(template.Timers.Select(t => t.Name), service.Timers.Select(t => t.Name));
    }

    [Fact]
    public void VAR_004_variables_are_copied_with_their_default_values()
    {
        var service = ServiceCopying.FromTemplate(SampleData.ServiceTemplate(), "x", Date);

        var variable = Assert.Single(service.Variables);
        Assert.Equal("preacher", variable.Name);
        Assert.Equal("TBD", variable.Value);
    }

    [Fact]
    public void AS_04_a_timer_expiry_jump_points_at_an_item_that_exists_in_the_new_service()
    {
        var template = SampleData.ServiceTemplate(); // TimerExpired(ServiceStart) -> GoToItem(item 201)
        var service = ServiceCopying.FromTemplate(template, "x", Date);

        var jump = Assert.IsType<GoToItem>(Assert.Single(Assert.Single(service.Actions).Commands));
        Assert.Equal(service.Items[1].Id, jump.ItemId);
        Assert.NotEqual(I<ServiceItem>(201), jump.ItemId);
    }

    [Fact]
    public void ACT_002_item_level_actions_are_copied_and_retargeted()
    {
        var service = ServiceCopying.FromTemplate(TemplateWithItemAction(), "x", Date);

        var action = Assert.Single(service.Items[0].Actions);
        Assert.Equal(service.Items[2].Id, Assert.IsType<GoToItem>(Assert.Single(action.Commands)).ItemId);
    }

    [Fact]
    public void SVC_035_the_template_is_never_changed_by_creating_or_editing_a_service()
    {
        var template = TemplateWithItemAction();
        var before = VersionedFile.Serialize(template);

        _ = ServiceCopying.FromTemplate(template, "x", Date);
        _ = ServiceCopying.FromTemplate(template, "y", Date);

        Assert.Equal(before, VersionedFile.Serialize(template));
    }

    [Fact]
    public void Two_services_from_the_same_template_do_not_share_ids()
    {
        var template = SampleData.ServiceTemplate();
        var a = ServiceCopying.FromTemplate(template, "a", Date);
        var b = ServiceCopying.FromTemplate(template, "b", Date);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Empty(a.Items.Select(i => i.Id).Intersect(b.Items.Select(i => i.Id)));
    }

    // ---- Duplicate service (SVC-003) -------------------------------------------------------

    [Fact]
    public void SVC_003_a_duplicate_service_gets_new_item_ids_and_retargeted_actions()
    {
        var original = SampleData.Service();
        var copy = ServiceCopying.Duplicate(original, "Copy", Date);

        var json = VersionedFile.Serialize(copy);
        foreach (var item in original.Items)
            Assert.DoesNotContain(item.Id.ToString(), json);

        var goTo = copy.Actions[0].Commands.OfType<GoToItem>().Single();
        Assert.Equal(copy.Items[2].Id, goTo.ItemId); // was item 102, the third item

        var slideInItem = copy.Actions[0].Commands.OfType<GoToSlide>().First().Target;
        Assert.Equal(copy.Items[0].Id, slideInItem.ItemId); // was item 100
        Assert.Equal(SampleData.Verse1, slideInItem.SlideId); // slide IDs belong to the Show and are unchanged

        Assert.Equal(copy.Items[1].Id, Assert.IsType<GoToItem>(copy.Actions[1].Commands[0]).ItemId); // was item 101
    }

    [Fact]
    public void SVC_003_a_duplicate_starts_in_rehearse_mode_and_keeps_variables_and_local_copies()
    {
        var original = SampleData.Service(); // Live
        var copy = ServiceCopying.Duplicate(original, "Copy", Date);

        Assert.Equal(ServiceMode.Live, original.Mode);
        Assert.Equal(ServiceMode.Rehearse, copy.Mode);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Copy", copy.Name);
        Assert.Equal(Date, copy.Date);
        Assert.Equal(original.CreatedFromServiceTemplateId, copy.CreatedFromServiceTemplateId);
        Assert.Equal("Pastor Sam", copy.Variables.Single(v => v.Name == "preacher").Value);
        Assert.Same(((ShowCopyItem)original.Items[1]).LocalShow, ((ShowCopyItem)copy.Items[1]).LocalShow);
    }

    [Fact]
    public void SVC_003_item_level_actions_on_a_duplicate_follow_the_new_item_ids()
    {
        var original = ServiceCopying.FromTemplate(TemplateWithItemAction(), "x", Date);
        var copy = ServiceCopying.Duplicate(original, "y", Date);

        var jump = Assert.IsType<GoToItem>(Assert.Single(Assert.Single(copy.Items[0].Actions).Commands));
        Assert.Equal(copy.Items[2].Id, jump.ItemId);
    }

    [Fact]
    public void Duplicating_leaves_the_original_service_unchanged()
    {
        var original = SampleData.Service();
        var before = VersionedFile.Serialize(original);

        _ = ServiceCopying.Duplicate(original, "Copy", Date);

        Assert.Equal(before, VersionedFile.Serialize(original));
    }
}
