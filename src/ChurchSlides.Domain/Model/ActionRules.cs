namespace ChurchSlides.Domain.Model;

/// <summary>Where an Action can live (ACT-002).</summary>
public enum ActionLevel { Slide, Show, ServiceItem, Service, ServiceTemplate }

public static class ActionRules
{
    /// <summary>Whether an Action with this trigger may be defined at this level (ACT-010..016).</summary>
    public static bool IsAllowed(ActionTrigger trigger, ActionLevel level) => trigger switch
    {
        ServiceStarted => level is ActionLevel.Service or ActionLevel.ServiceTemplate,
        ShowStarted or ShowEnded => level is ActionLevel.Show or ActionLevel.ServiceItem,
        SlideDisplayed or SlideEnded => level is ActionLevel.Slide or ActionLevel.ServiceItem,
        TimerExpired => level is ActionLevel.Slide or ActionLevel.Service or ActionLevel.ServiceTemplate,
        AudioEnded => level is ActionLevel.Show,
        _ => false,
    };
}
