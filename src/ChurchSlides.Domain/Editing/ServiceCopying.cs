using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Editing;

/// <summary>Creating Services from Service Templates and duplicating Services (SVC-003, SVC-030, SVC-035, VAR-004).</summary>
public static class ServiceCopying
{
    /// <summary>
    /// A new Rehearse-mode Service from a template. Service Item IDs are new, and Actions that name items
    /// (GoToItem, GoToSlide with an item) are retargeted to them. Variables keep their default values.
    /// The template itself is never touched (SVC-035).
    /// </summary>
    public static Service FromTemplate(ServiceTemplate template, string name, DateOnly date)
    {
        var ids = RegisterItems(template.Items);
        return new Service
        {
            Id = Id<Service>.New(),
            Name = name,
            Date = date,
            Mode = ServiceMode.Rehearse,
            Items = [.. template.Items.Select(item => Remap(item, ids))],
            Variables = template.Variables,
            Timers = template.Timers,
            Actions = ids.Map(template.Actions),
            CreatedFromServiceTemplateId = template.Id,
        };
    }

    /// <summary>
    /// A copy of a Service with new Service Item IDs and retargeted Actions. It starts in Rehearse mode, so copying a
    /// Live Service can't produce a second CCLI log. Service-local Show copies keep their inner IDs.
    /// </summary>
    public static Service Duplicate(Service service, string name, DateOnly date)
    {
        var ids = RegisterItems(service.Items);
        return service with
        {
            Id = Id<Service>.New(),
            Name = name,
            Date = date,
            Mode = ServiceMode.Rehearse,
            Items = [.. service.Items.Select(item => Remap(item, ids))],
            Actions = ids.Map(service.Actions),
        };
    }

    private static IdRemapper RegisterItems(IEnumerable<ServiceItem> items)
    {
        var ids = new IdRemapper();
        foreach (var item in items) ids.RegisterItem(item.Id);
        return ids;
    }

    private static ServiceItem Remap(ServiceItem item, IdRemapper ids) =>
        item with { Id = ids.Map(item.Id), Actions = ids.Map(item.Actions) };
}
