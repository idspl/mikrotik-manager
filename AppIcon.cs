namespace IndigoRouterScheduler;

internal static class AppIcon
{
    public static Icon Current { get; } =
        Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
}
