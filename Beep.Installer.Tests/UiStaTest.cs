using System.Runtime.ExceptionServices;

namespace Beep.Installer.Tests;

internal static class UiStaTest
{
    internal static void Pump(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1);
        }
        if (!task.IsCompleted) throw new TimeoutException("The owned UI operation did not complete.");
        task.GetAwaiter().GetResult();
        System.Windows.Forms.Application.DoEvents();
    }

    internal static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // The dialogs finish background reads with "await", which returns to the UI thread through the
            // thread's Windows Forms synchronization context. An application keeps that context for as long as
            // its message loop runs. These tests pump with Application.DoEvents instead, and Windows Forms
            // removes an automatically installed context each time such a pump ends: the next "await" then
            // finished on a pool thread, created controls there, and the test waited forever on a window whose
            // thread never answers. The context is installed here once, by hand, and kept for the whole test.
            System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // These cases exercise complete multi-language native-window matrices, not a UI latency budget.
        // A timeout must end the dedicated test host: continuing with a live STA worker corrupts
        // process-wide culture and focus state for every subsequent collection.
        if (!thread.Join(TimeSpan.FromMinutes(3)))
            Environment.FailFast("A native UI test exceeded its three-minute matrix deadline. The test host is stopped to prevent a live STA worker from overlapping later tests.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
