using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Forms;

/// <summary>
/// Bridges the Beep Setup Wizard to the WinForms installer UI.
/// Implements <see cref="ISetupWizardAdapter"/> to receive step/progress/result callbacks.
/// </summary>
public class WinFormsInstallerAdapter : ISetupWizardAdapter
{
    private readonly InstallerMainForm _form;

    public WinFormsInstallerAdapter(InstallerMainForm form)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
    }

    public Task<SetupReport> RunAsync(ISetupWizard wizard, SetupContext context, CancellationToken cancellationToken = default)
    {
        var progress = new Progress<PassedArgs>(args =>
        {
            if (_form.IsDisposed || _form.Disposing) return;
            try { _form.Invoke(() => _form.UpdateProgress(args)); } catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

        return Task.Run(() =>
        {
            try
            {
                wizard.Run(context, progress);
                var report = wizard.GetReport();
                try { _form.Invoke(() => _form.ShowResult(report)); } catch (ObjectDisposedException) { }
                return report;
            }
            catch (OperationCanceledException)
            {
                var report = wizard.GetReport();
                try { _form.Invoke(() => _form.ShowResult(report)); } catch (ObjectDisposedException) { }
                return report;
            }
        }, cancellationToken);
    }

    public void ShowStep(ISetupStep step, int stepIndex, int totalSteps)
    {
        if (_form.IsDisposed || _form.Disposing) return;
        try { _form.Invoke(() => _form.ShowStep(step.StepName, stepIndex + 1, totalSteps)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void ShowProgress(string stepId, int percentComplete, string message)
    {
        if (_form.IsDisposed || _form.Disposing) return;
        try
        {
            _form.Invoke(() => _form.UpdateProgress(new PassedArgs
            {
                Messege = message,
                ParameterInt1 = percentComplete
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void ShowResult(SetupReport report)
    {
        if (_form.IsDisposed || _form.Disposing) return;
        try { _form.Invoke(() => _form.ShowResult(report)); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
