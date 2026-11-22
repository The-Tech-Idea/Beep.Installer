namespace Beep.Installer.Ui;

internal sealed record GuidedAuthoringStep(string Id, string TitleKey, string Title, string[] Sections, bool Optional = false);
