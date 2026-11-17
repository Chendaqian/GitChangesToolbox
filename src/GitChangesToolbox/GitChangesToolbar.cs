using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitChangesToolbox
{
    /// <summary>
    /// 在 Git 更改窗口顶部工具栏注入折叠和定位按钮。
    /// </summary>
    internal sealed class GitChangesToolbar
    {
        private const string PanelName = "buttonPanel";
        private const string CollapseTag = "GitChangesToolboxCollapseButton";
        private const string LocateTag = "GitChangesToolboxLocateButton";

        private readonly IServiceProvider _serviceProvider;
        private DispatcherTimer _timer;
        private Button _collapseButton;
        private CrispImage _collapseIcon;

        public GitChangesToolbar(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Start()
        {
            EnsureButtons();

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            EnsureButtons();
        }

        private void EnsureButtons()
        {
            try
            {
                DependencyObject root = GitChangesController.FindGitWindowRoot(_serviceProvider);
                if (root == null)
                    return;

                StackPanel panel = FindByName(root, PanelName) as StackPanel;
                if (panel == null)
                    return;

                if (HasTaggedButton(panel, CollapseTag))
                    return;

                Button collapseButton = CreateIconButton(
                    new CrispImage { Moniker = KnownMonikers.CollapseAll },
                    "折叠更改列表中的所有文件夹",
                    CollapseTag,
                    OnCollapseClick);
                Button locateButton = CreateIconButton(
                    new CrispImage { Moniker = KnownMonikers.LocateControl },
                    "定位到当前打开的文件并选中",
                    LocateTag,
                    OnLocateClick);

                _collapseButton = collapseButton;
                _collapseIcon = collapseButton.Content as CrispImage;

                panel.Children.Insert(0, locateButton);
                panel.Children.Insert(0, collapseButton);
            }
            catch (Exception)
            {
            }
        }

        private Button CreateIconButton(CrispImage icon, string toolTip, string tag, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = icon,
                Tag = tag,
                ToolTip = toolTip,
                Style = CreateToolbarButtonStyle(),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false
            };
            button.Click += handler;

            return button;
        }

        private static Style CreateToolbarButtonStyle()
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3)));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 24.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24.0));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowButtonHoverActiveBrushKey)));
            style.Triggers.Add(hover);

            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowButtonDownBrushKey)));
            style.Triggers.Add(pressed);

            return style;
        }

        private void OnCollapseClick(object sender, RoutedEventArgs e)
        {
            bool hasExpanded = GitChangesController.HasExpandedNodes(_serviceProvider);
            if (hasExpanded)
            {
                int collapsed = GitChangesController.CollapseAll(_serviceProvider);
                UpdateCollapseButton(true);
                ShowStatus(collapsed > 0 ? $"已折叠 {collapsed} 项" : "没有可折叠的项");
                return;
            }

            int expanded = GitChangesController.ExpandAll(_serviceProvider);
            UpdateCollapseButton(false);
            ShowStatus(expanded > 0 ? $"已展开 {expanded} 项" : "没有可展开的项");
        }

        private void UpdateCollapseButton(bool collapsed)
        {
            if (_collapseIcon != null)
                _collapseIcon.Moniker = collapsed ? KnownMonikers.ExpandAll : KnownMonikers.CollapseAll;

            if (_collapseButton != null)
                _collapseButton.ToolTip = collapsed ? "展开更改列表中的所有文件夹" : "折叠更改列表中的所有文件夹";
        }

        private void OnLocateClick(object sender, RoutedEventArgs e)
        {
            string filePath = GitChangesController.GetActiveDocumentPath(_serviceProvider);
            if (string.IsNullOrEmpty(filePath))
            {
                ShowStatus("当前没有打开的文件");
                return;
            }

            bool located = GitChangesController.Locate(_serviceProvider, filePath);
            ShowStatus(located ? "已定位到当前文件" : "当前文件不在更改列表中");
        }

        private void ShowStatus(string text)
        {
            try
            {
                IVsStatusbar statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
                if (statusBar != null)
                    statusBar.SetText(text);
            }
            catch (Exception)
            {
            }
        }

        private static bool HasTaggedButton(StackPanel panel, string tag)
        {
            foreach (UIElement child in panel.Children)
            {
                Button button = child as Button;
                if (button != null && string.Equals(button.Tag as string, tag, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static FrameworkElement FindByName(DependencyObject element, string name)
        {
            if (element == null)
                return null;

            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null && string.Equals(frameworkElement.Name, name, StringComparison.Ordinal))
                return frameworkElement;

            if (!(element is System.Windows.Media.Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return null;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                FrameworkElement found = FindByName(System.Windows.Media.VisualTreeHelper.GetChild(element, i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}