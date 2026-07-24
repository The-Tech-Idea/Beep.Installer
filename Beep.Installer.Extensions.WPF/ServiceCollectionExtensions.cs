using Microsoft.Extensions.DependencyInjection;
using TheTechIdea.Beep.Updates;

namespace Beep.Installer.Extensions.WPF
{
    /// <summary>
    /// DI registration for the WPF update experience. It forwards to the BeepDM
    /// <c>AddBeepAppUpdates</c> so <see cref="IAppUpdateService"/> resolves — the UI itself is the
    /// static <see cref="BeepUpdater"/>, which the app calls with the resolved service.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>Registers app self-update, loading <c>update-settings.json</c> beside the app.</summary>
        public static IServiceCollection AddBeepAppUpdatesWpf(this IServiceCollection services, string? settingsPath = null)
            => services.AddBeepAppUpdates(settingsPath);

        /// <summary>Registers app self-update with explicit settings.</summary>
        public static IServiceCollection AddBeepAppUpdatesWpf(this IServiceCollection services, UpdateSettings settings)
            => services.AddBeepAppUpdates(settings);
    }
}
