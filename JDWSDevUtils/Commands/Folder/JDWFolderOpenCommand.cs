using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell.Interop;
using JDWSDevUtils.Language;
using System.Globalization;
using System.Diagnostics;
using System.IO; 


namespace JDWSDevUtils.Commands
{
    internal sealed class JDWFolderOpenCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("8633843e-4e34-4da7-abea-f70099dd00ca");
        private readonly AsyncPackage package;
        private readonly OleMenuCommand menuItem; // 커맨드 객체 저장

        private JDWFolderOpenCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            var menuCommandID = new CommandID(CommandSet, CommandId);

            // OleMenuCommand 생성 및 이벤트 핸들러 연결
            this.menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            this.menuItem.BeforeQueryStatus += this.OnBeforeQueryStatus;

            commandService.AddCommand(this.menuItem);

        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            // UI 스레드인지 확인 (리소스 접근 등 위해)
            ThreadHelper.ThrowIfNotOnUIThread();

            var command = sender as OleMenuCommand;

            if (command != null)
            {
                try
                {
                    // *** 여기서 지역화된 텍스트 설정 ***
                    command.Text = Resource.OpenAllCS; //

                    command.Properties["ToolTip"] = Resource.OpenAllCSTooltip;
                    // 필요에 따라 Visible, Enabled 상태 설정
                    command.Visible = true;
                    command.Enabled = true; // 우선 항상 활성화
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BeforeQueryStatus] Failed to set text/tooltip: {ex.ToString()}");
                    // 오류 발생 시 기본 텍스트
                    command.Text = "Open All .cs (Error)";
                    command.Visible = true;
                    command.Enabled = true; // 오류 시에도 보이도록 할지 결정
                }
            }
        }


        public static async Task InitializeAsync(AsyncPackage package)
        {
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new JDWFolderOpenCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            // Ensure we are on the UI thread, as we are interacting with DTE potentially
            ThreadHelper.ThrowIfNotOnUIThread();

            // Get DTE service using the service provider from the package
            var dte = (this.package as IServiceProvider).GetService(typeof(SDTE)) as DTE2;
            if (dte == null)
            {
                VsShellUtilities.ShowMessageBox(this.package, "Cannot get DTE service.", "Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // Get selected folder from Solution Explorer
            UIHierarchy solutionExplorer = dte.ToolWindows.SolutionExplorer;
            var selectedItems = (Array)solutionExplorer.SelectedItems;
            if (selectedItems == null || selectedItems.Length == 0) return; // Nothing selected

            UIHierarchyItem selectedHierarchyItem = selectedItems.GetValue(0) as UIHierarchyItem;
            ProjectItem selectedProjectItem = selectedHierarchyItem?.Object as ProjectItem;


            // Check if it's a physical folder
            if (selectedProjectItem?.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder)
            {
                string folderPath = null;
                try
                {
                    folderPath = selectedProjectItem.Properties?.Item("FullPath")?.Value?.ToString();
                }
                catch (ArgumentException) { /* Property might not exist */ }

                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    VsShellUtilities.ShowMessageBox(this.package, "Could not get valid folder path.", "Warning",
                        OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }


                try
                {
                    string[] csFiles = Directory.GetFiles(folderPath, "*.cs", SearchOption.TopDirectoryOnly);

                    if (csFiles.Length > 0)
                    {
                        Window firstWindow = null;
                        try
                        {
                            // 1. Open the first file
                            System.Diagnostics.Debug.WriteLine($"Opening first file: {csFiles[0]}");
                            firstWindow = dte.ItemOperations.OpenFile(csFiles[0]);
                            // Give VS a moment to process the window opening
                            // System.Threading.Thread.Sleep(200); // Delay - uncomment/adjust if needed
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error opening first file '{csFiles[0]}': {ex.Message}");
                            VsShellUtilities.ShowMessageBox(this.package, $"Error opening first file:\n{ex.Message}", "Error",
                                OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                            // Optionally open remaining files in the default window if the first fails?
                            // For now, just return.
                            return;
                        }

                        if (firstWindow != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"First window obtained: {firstWindow.Caption}");
                            try
                            {
                                // 2. Make the window float (if not already)
                                if (!firstWindow.IsFloating)
                                {
                                    System.Diagnostics.Debug.WriteLine("Making window float...");
                                    firstWindow.IsFloating = true;
                                    // System.Threading.Thread.Sleep(200); // Delay - uncomment/adjust if needed
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Window is already floating.");
                                }

                                // 3. Activate the window (important!)
                                System.Diagnostics.Debug.WriteLine("Activating window...");
                                firstWindow.Activate();
                                // System.Threading.Thread.Sleep(100); // Delay - uncomment/adjust if needed

                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error floating or activating window: {ex.Message}");
                                // Continue trying to open files in the default window?
                                VsShellUtilities.ShowMessageBox(this.package, $"Could not make window float:\n{ex.Message}", "Warning",
                                    OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                            }


                            // 4. Open remaining files
                            for (int i = 1; i < csFiles.Length; i++)
                            {
                                try
                                {
                                    // *** 추가: 파일을 열기 직전에 부동 창을 다시 활성화 ***
                                    if (firstWindow != null && firstWindow.IsFloating) // 창이 유효하고 떠 있는지 확인
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Activating floating window before opening: {csFiles[i]}");
                                        firstWindow.Activate();
                                        // System.Threading.Thread.Sleep(50); // 활성화 후 아주 잠깐 대기 (필요 시 조절)
                                    }

                                    System.Diagnostics.Debug.WriteLine($"Opening subsequent file: {csFiles[i]}");
                                    dte.ItemOperations.OpenFile(csFiles[i]);
                                    // System.Threading.Thread.Sleep(50); // 파일 열기 후 아주 잠깐 대기 (필요 시 조절)
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error opening subsequent file '{csFiles[i]}': {ex.Message}");
                                    // Log or show error for this specific file, but continue with others
                                }
                            }
                            System.Diagnostics.Debug.WriteLine("Finished opening files.");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to get first window object.");
                            // Fallback: Open all files in the default window if first one failed to give a Window object
                            VsShellUtilities.ShowMessageBox(this.package, "Could not get first window object. Opening files in default location.", "Info",
                                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                            for (int i = 1; i < csFiles.Length; i++) // Start from 1 as 0 was already attempted
                            {
                                try { dte.ItemOperations.OpenFile(csFiles[i]); } catch { /* Ignore error */ }
                            }
                        }
                    }
                    else
                    {
                        VsShellUtilities.ShowMessageBox(this.package, "No .cs files found in the selected folder.", "Info",
                            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }
                catch (Exception ex) // Catch errors getting files or general execution errors
                {
                    System.Diagnostics.Debug.WriteLine($"Error executing command: {ex.Message}");
                    VsShellUtilities.ShowMessageBox(
                        this.package, // Use the package instance
                        $"An error occurred: {ex.Message}",
                        "Folder C# File Opener Error",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
            // else: Silently ignore if the selected item is not a folder
        }

    }
}
