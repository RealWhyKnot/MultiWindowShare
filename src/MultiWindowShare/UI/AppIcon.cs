namespace MultiWindowShare.UI;

// Forms do not inherit ApplicationIcon, and the embedded copy keeps every frame so Windows can
// pick the right size for the title bar, the taskbar, and alt-tab.
internal static class AppIcon
{
    public static Icon? Value { get; } = Load();

    private static Icon? Load()
    {
        using Stream? stream = typeof(AppIcon).Assembly
            .GetManifestResourceStream("MultiWindowShare.Assets.AppIcon.ico");
        return stream is null ? null : new Icon(stream);
    }
}
