using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace GitChangesToolbox
{
    /// <summary>
    /// 通过反射定位 Git 更改窗口的更改列表，并提供折叠与定位能力。
    /// </summary>
    internal static class GitChangesController
    {
        private const string FileListViewTypeName = "FileListView";
        private const string InnerListViewPropertyName = "InnerListViewControl";

        /// <summary>
        /// 折叠所有更改列表中的文件夹节点。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <returns>实际折叠的节点数。</returns>
        public static int CollapseAll(IServiceProvider serviceProvider)
        {
            StringBuilder log = new StringBuilder();
            DependencyObject root = FindGitWindowRoot(serviceProvider);
            log.AppendLine($"Git window root: {root != null}");
            if (root == null)
            {
                WriteDiagnosticLog(log);
                return 0;
            }

            List<object> views = CollectFileListViews(root);
            log.AppendLine($"FileListView count: {views.Count}");

            int collapsed = 0;
            foreach (object view in views)
            {
                object viewModel = GetProperty(view, "DataContext");
                List<object> nodes = CollectAllNodes(view);
                log.AppendLine($"before count={SnapshotItems(view).Count}, all nodes={nodes.Count}");
                log.AppendLine($"view model={DescribeType(viewModel)}");

                int invoked = 0;
                foreach (object node in nodes)
                {
                    if (GetProperty(node, "IsExpanded") as bool? != true)
                        continue;

                    if (TryExecuteExpanderCommand(viewModel, node, log))
                        invoked++;
                }

                PumpDispatcher();
                List<object> after = SnapshotItems(view);
                log.AppendLine($"after expander command count={after.Count}, invoked={invoked}, collapsed={collapsed}");

                if (after.Count >= nodes.Count && invoked == 0)
                    log.AppendLine("nothing collapsed");

                collapsed += invoked;
            }

            WriteDiagnosticLog(log);

            return collapsed;
        }

        /// <summary>
        /// 展开更改列表中的所有文件夹节点。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <returns>实际展开的节点数。</returns>
        public static int ExpandAll(IServiceProvider serviceProvider)
        {
            StringBuilder log = new StringBuilder();
            DependencyObject root = FindGitWindowRoot(serviceProvider);
            if (root == null)
            {
                WriteDiagnosticLog(log);
                return 0;
            }

            int expanded = 0;
            foreach (object view in CollectFileListViews(root))
            {
                object viewModel = GetProperty(view, "DataContext");
                List<object> nodes = CollectAllNodes(view);
                log.AppendLine($"expand: all nodes={nodes.Count}");

                foreach (object node in nodes)
                {
                    if (!HasChildren(node))
                        continue;

                    if (GetProperty(node, "IsExpanded") as bool? == true)
                        continue;

                    if (TryExecuteExpanderCommand(viewModel, node, log))
                        expanded++;
                }

                PumpDispatcher();
                log.AppendLine($"expanded={expanded}, visible={SnapshotItems(view).Count}");
            }

            WriteDiagnosticLog(log);

            return expanded;
        }

        /// <summary>
        /// 判断更改列表中是否还有展开的文件夹节点。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <returns>存在展开节点返回 true。</returns>
        public static bool HasExpandedNodes(IServiceProvider serviceProvider)
        {
            DependencyObject root = FindGitWindowRoot(serviceProvider);
            if (root == null)
                return false;

            foreach (object view in CollectFileListViews(root))
            {
                foreach (object node in CollectAllNodes(view))
                {
                    if (HasChildren(node) && GetProperty(node, "IsExpanded") as bool? == true)
                        return true;
                }
            }

            return false;
        }

        private static List<object> CollectAllNodes(object view)
        {
            List<object> nodes = new List<object>(64);
            HashSet<object> seen = new HashSet<object>();
            foreach (object node in SnapshotItems(view))
                WalkNode(node, nodes, seen);

            nodes.Sort(CompareByIndentLevel);

            return nodes;
        }

        private static void WalkNode(object node, List<object> nodes, HashSet<object> seen)
        {
            if (node == null || !seen.Add(node))
                return;

            nodes.Add(node);

            IEnumerable children = GetProperty(node, "Children") as IEnumerable;
            if (children == null)
                return;

            foreach (object child in children)
                WalkNode(child, nodes, seen);
        }

        private static int CompareByIndentLevel(object left, object right)
        {
            return GetIndentLevel(left).CompareTo(GetIndentLevel(right));
        }

        private static bool HasChildren(object node)
        {
            object value = GetProperty(node, "HasChildren");
            if (value is bool)
                return (bool)value;

            return GetCollectionCount(GetProperty(node, "Children")) > 0;
        }

        private static bool TryExecuteExpanderCommand(object viewModel, object node, StringBuilder log)
        {
            object command = GetProperty(viewModel, "ExpanderCommand");
            if (command == null)
            {
                if (log != null)
                    log.AppendLine("ExpanderCommand is null");
                return false;
            }

            MethodInfo execute = FindExecuteMethod(command);
            if (execute == null)
            {
                if (log != null)
                    log.AppendLine($"no Execute method on {command.GetType().FullName}");
                return false;
            }

            try
            {
                execute.Invoke(command, new object[] { node });
                return true;
            }
            catch (Exception ex)
            {
                if (log != null)
                    log.AppendLine($"ExpanderCommand failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static MethodInfo FindExecuteMethod(object command)
        {
            foreach (MethodInfo method in command.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "Execute")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1)
                    return method;
            }

            return null;
        }

        private static void RebindItems(object view, object items, StringBuilder log)
        {
            if (items == null)
            {
                if (log != null)
                    log.AppendLine("vm items is null, skip rebind");
                return;
            }

            bool viewSet = SetProperty(view, "ItemsSource", items);
            object inner = GetProperty(view, InnerListViewPropertyName);
            bool innerSet = SetProperty(inner, "ItemsSource", items);

            if (log != null)
                log.AppendLine($"set FileListView.ItemsSource: {viewSet}, InnerListView.ItemsSource: {innerSet}");
        }

        private static void ForceItemsRefresh(object view, StringBuilder log)
        {
            object inner = GetProperty(view, InnerListViewPropertyName);
            ItemsControl itemsControl = inner as ItemsControl;
            if (itemsControl == null)
            {
                log.AppendLine("inner is not ItemsControl");
                return;
            }

            try
            {
                itemsControl.Items.Refresh();
                log.AppendLine("Items.Refresh ok");
            }
            catch (Exception ex)
            {
                log.AppendLine($"Items.Refresh failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static int GetCollectionCount(object value)
        {
            ICollection collection = value as ICollection;
            if (collection != null)
                return collection.Count;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
                return -1;

            int count = 0;
            foreach (object item in enumerable)
                count++;

            return count;
        }

        private static void LogToggleDiagnostics(object view, StringBuilder log)
        {
            FrameworkElement element = view as FrameworkElement;
            if (element == null)
            {
                log.AppendLine("view is not a FrameworkElement");
                return;
            }

            List<ToggleButton> toggles = new List<ToggleButton>(32);
            CollectAllToggles(element, toggles);
            log.AppendLine($"toggle total: {toggles.Count}");

            int index = 0;
            foreach (ToggleButton toggle in toggles)
            {
                if (index >= 10)
                    break;

                object dataContext = toggle.DataContext;
                log.AppendLine($"  toggle[{index}] dc={DescribeType(dataContext)} checked={toggle.IsChecked}");
                index++;
            }
        }

        private static void CollectAllToggles(DependencyObject element, List<ToggleButton> results)
        {
            if (element == null)
                return;

            ToggleButton toggle = element as ToggleButton;
            if (toggle != null)
                results.Add(toggle);

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                CollectAllToggles(VisualTreeHelper.GetChild(element, i), results);
        }

        private static int ClickExpandedToggles(object view)
        {
            FrameworkElement element = view as FrameworkElement;
            if (element == null)
                return 0;

            List<ToggleButton> toggles = new List<ToggleButton>(32);
            CollectExpandToggles(element, toggles);
            toggles.Sort(CompareToggles);

            int clicked = 0;
            foreach (ToggleButton toggle in toggles)
            {
                if (GetProperty(toggle.DataContext, "IsExpanded") as bool? != true)
                    continue;

                try
                {
                    toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, toggle));
                    clicked++;
                }
                catch (Exception)
                {
                }
            }

            return clicked;
        }

        private static int CompareToggles(ToggleButton left, ToggleButton right)
        {
            return GetIndentLevel(left.DataContext).CompareTo(GetIndentLevel(right.DataContext));
        }

        private static int GetIndentLevel(object node)
        {
            object value = GetProperty(node, "IndentLevel");
            if (value is int)
                return (int)value;

            return 0;
        }

        private static void CollectExpandToggles(DependencyObject element, List<ToggleButton> results)
        {
            if (element == null)
                return;

            ToggleButton toggle = element as ToggleButton;
            if (toggle != null && toggle.DataContext != null && toggle.DataContext.GetType().Name == "GitPendingChangeNode")
                results.Add(toggle);

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                CollectExpandToggles(VisualTreeHelper.GetChild(element, i), results);
        }

        /// <summary>
        /// 在更改列表中定位并选中指定文件。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <param name="filePath">文件完整路径。</param>
        /// <returns>找到并选中返回 true。</returns>
        public static bool Locate(IServiceProvider serviceProvider, string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            DependencyObject root = FindGitWindowRoot(serviceProvider);
            if (root == null)
                return false;

            foreach (object view in CollectFileListViews(root))
            {
                List<object> ancestors = new List<object>(8);
                object match = FindNode(SnapshotItems(view), filePath, ancestors);
                if (match == null)
                    continue;

                object viewModel = GetProperty(view, "DataContext");

                foreach (object ancestor in ancestors)
                {
                    if (GetProperty(ancestor, "IsExpanded") as bool? == true)
                        continue;

                    if (!TryExecuteExpanderCommand(viewModel, ancestor, null))
                        SetProperty(ancestor, "IsExpanded", true);
                }

                PumpDispatcher();

                SetProperty(match, "IsSelected", true);

                object inner = GetProperty(view, InnerListViewPropertyName);
                if (inner != null)
                {
                    SetProperty(inner, "SelectedItem", match);
                    InvokeScrollIntoView(inner, match);
                }

                PumpDispatcher();

                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取当前活动文档的完整路径。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <returns>文档路径，没有活动文档时返回空字符串。</returns>
        public static string GetActiveDocumentPath(IServiceProvider serviceProvider)
        {
            try
            {
                EnvDTE.DTE dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null || dte.ActiveDocument == null)
                    return string.Empty;

                return dte.ActiveDocument.FullName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 查找 Git 更改窗口的 WPF 根元素。
        /// </summary>
        /// <param name="serviceProvider">VS 服务提供者。</param>
        /// <returns>WPF 根元素，未找到返回 null。</returns>
        public static DependencyObject FindGitWindowRoot(IServiceProvider serviceProvider)
        {
            IVsUIShell uiShell = serviceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
                return null;

            IEnumWindowFrames enumWindowFrames = null;
            int hr = uiShell.GetToolWindowEnum(out enumWindowFrames);
            if (hr != 0 || enumWindowFrames == null)
                return null;

            IVsWindowFrame[] frames = new IVsWindowFrame[1];
            uint fetched = 0;
            while (enumWindowFrames.Next(1, frames, out fetched) == 0 && fetched == 1)
            {
                IVsWindowFrame frame = frames[0];
                object docView = GetFrameValue(frame, __VSFPROPID.VSFPROPID_DocView);
                if (docView == null)
                    continue;

                string typeName = docView.GetType().FullName ?? string.Empty;
                string caption = GetFrameValue(frame, __VSFPROPID.VSFPROPID_Caption) as string ?? string.Empty;

                bool isGitWindow = typeName.IndexOf("GitToolWindow", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isGitWindow && caption.IndexOf("Git", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DependencyObject root = GetWpfRoot(docView);
                if (root != null)
                    return root;
            }

            return null;
        }

        private static List<object> CollectFileListViews(DependencyObject root)
        {
            List<object> results = new List<object>(2);
            CollectByTypeName(root, FileListViewTypeName, results);

            return results;
        }

        private static void CollectByTypeName(DependencyObject element, string typeName, List<object> results)
        {
            if (element == null)
                return;

            if (element.GetType().Name == typeName)
                results.Add(element);

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                CollectByTypeName(VisualTreeHelper.GetChild(element, i), typeName, results);
        }

        private static List<object> SnapshotItems(object view)
        {
            IEnumerable itemsSource = GetProperty(view, "ItemsSource") as IEnumerable;
            if (itemsSource == null)
                return new List<object>(0);

            List<object> nodes = new List<object>(32);
            foreach (object node in itemsSource)
                nodes.Add(node);

            return nodes;
        }

        private static object FindNode(IEnumerable items, string filePath, List<object> ancestors)
        {
            foreach (object node in items)
            {
                if (NodeMatches(node, filePath))
                    return node;

                IEnumerable children = GetProperty(node, "Children") as IEnumerable;
                if (children == null)
                    continue;

                ancestors.Add(node);
                object found = FindNode(children, filePath, ancestors);
                if (found != null)
                    return found;

                ancestors.RemoveAt(ancestors.Count - 1);
            }

            return null;
        }

        private static bool NodeMatches(object node, string filePath)
        {
            string itemPath = GetProperty(node, "ItemPath") as string;
            if (string.IsNullOrEmpty(itemPath))
                return false;

            return string.Equals(NormalizePath(itemPath), NormalizePath(filePath), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.Replace('/', '\\').TrimEnd('\\');
        }

        private static void InvokeScrollIntoView(object listView, object item)
        {
            MethodInfo scrollMethod = listView.GetType().GetMethod("ScrollIntoView", new Type[] { typeof(object) });
            if (scrollMethod == null)
                return;

            try
            {
                scrollMethod.Invoke(listView, new object[] { item });
            }
            catch (Exception)
            {
            }
        }

        private static DependencyObject GetWpfRoot(object docView)
        {
            DependencyObject element = docView as DependencyObject;
            if (element != null)
                return element;

            ToolWindowPane pane = docView as ToolWindowPane;
            if (pane != null)
                return pane.Content as DependencyObject;

            return null;
        }

        private static object GetFrameValue(IVsWindowFrame frame, __VSFPROPID propertyId)
        {
            object value;
            int hr = frame.GetProperty((int)propertyId, out value);
            if (hr != 0)
                return null;

            return value;
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null)
                return null;

            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
                return null;

            try
            {
                return property.GetValue(target);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SetProperty(object target, string name, object value)
        {
            if (target == null)
                return false;

            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
                return false;

            object current;
            try
            {
                current = property.GetValue(target);
            }
            catch (Exception)
            {
                current = null;
            }

            if (object.Equals(current, value))
                return false;

            try
            {
                property.SetValue(target, value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void PumpDispatcher()
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.Invoke(delegate { }, DispatcherPriority.Render);
            dispatcher.Invoke(delegate { }, DispatcherPriority.Background);
        }

        private static string DescribeType(object instance)
        {
            return instance == null ? "(null)" : instance.GetType().FullName;
        }

        private static void WriteDiagnosticLog(StringBuilder log)
        {
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "GitChangesToolbox");
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, "collapse-diagnostic.txt");
                File.AppendAllText(filePath, $"---- {DateTime.Now:HH:mm:ss} ----{Environment.NewLine}{log}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }
    }
}