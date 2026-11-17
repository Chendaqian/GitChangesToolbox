using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace GitChangesToolbox
{
    /// <summary>
    /// 扩展入口，在打开解决方案时加载并向 Git 更改窗口注入按钮。
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Git Changes Toolbox", "为 Git 更改窗口增加折叠和定位按钮", "1.0")]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class GitChangesToolboxPackage : AsyncPackage
    {
        public const string PackageGuidString = "8f2c1a7e-4b3d-4c1e-9a55-6d7e8f9a0b11";

        private GitChangesToolbar _toolbar;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _toolbar = new GitChangesToolbar(this);
            _toolbar.Start();
        }
    }
}