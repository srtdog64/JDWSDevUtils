using EnvDTE; // DTE 사용 위해 필요
using EnvDTE80; // DTE2 사용 위해 필요
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.IO; // Path, File 클래스 사용 위해 필요
using System.Text; // StringBuilder, Encoding 클래스 사용 위해 필요
using System.Threading.Tasks;
using System.Windows; // Clipboard 클래스 사용 위해 필요 (PresentationCore 참조 추가 필요)
using Task = System.Threading.Tasks.Task; // System.Threading.Tasks.Task 명시

namespace JDWSDevUtils.Commands
{
    internal sealed class JDWCopyScriptCommand
    {
        public const int CommandId = 0x0200;

        public static readonly Guid CommandSet = new Guid(JDWSDevUtilsPackage.PackageGuidString);

        private readonly AsyncPackage package;

        // 생성자: 커맨드 서비스에 명령 등록
        private JDWCopyScriptCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        // 싱글톤 인스턴스 (필요 시 사용)
        public static JDWCopyScriptCommand Instance { get; private set; }

        // 서비스 프로바이더
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => this.package;

        // 비동기 초기화 메서드 (패키지에서 호출)
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                Instance = new JDWCopyScriptCommand(package, commandService);
            }
        }

        // --- ★ Execute 메서드 구현 ★ ---
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // UI 스레드 확인

            // --- 1. DTE 서비스 가져오기 ---
            var dte = (this.package as IServiceProvider)?.GetService(typeof(SDTE)) as DTE2;
            if (dte == null)
            {
                VsShellUtilities.ShowMessageBox(this.package, "Cannot get DTE service.", "Error", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // --- 2. 선택된 항목들 가져오기 ---
            UIHierarchy solutionExplorer = dte.ToolWindows.SolutionExplorer;
            var selectedItems = (Array)solutionExplorer.SelectedItems;
            if (selectedItems == null || selectedItems.Length == 0)
            {
                // 선택된 항목이 없으면 아무것도 하지 않음
                return;
            }

            // --- 3. 선택된 C# 파일 내용 취합 ---
            StringBuilder contentBuilder = new StringBuilder();
            int filesCopied = 0;
            long totalSize = 0;

            try // DTE 객체 접근 및 파일 처리 중 예외 발생 가능성 있음
            {
                foreach (UIHierarchyItem hierItem in selectedItems) // 선택된 모든 항목 순회
                {
                    ProjectItem projItem = hierItem?.Object as ProjectItem;
                    if (projItem == null) continue; // 프로젝트 항목이 아니면 건너뛰기

                    // 실제 파일인지 확인
                    if (projItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                    {
                        string filePath = null;
                        try
                        {
                            filePath = projItem.Properties?.Item("FullPath")?.Value?.ToString();
                        }
                        catch (ArgumentException) { /* FullPath 속성 없음 */ continue; }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error getting FullPath: {ex.Message}"); continue; }

                        // 경로 유효성 및 .cs 확장자 확인
                        if (!string.IsNullOrEmpty(filePath) &&
                            File.Exists(filePath) &&
                            Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                        {
                            // --- 4. 파일 내용 읽기 (UTF-8 명시) ---
                            try
                            {
                                //UTF-8로 명시
                                string fileContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                                string fileName = Path.GetFileName(filePath);

                                // 구분자 추가
                                if (filesCopied > 0) // 첫 번째 파일이 아니면 앞에 빈 줄 추가
                                {
                                    contentBuilder.AppendLine();
                                }
                                contentBuilder.AppendLine($"// ===== Start: {fileName} =====");
                                contentBuilder.AppendLine(fileContent);
                                contentBuilder.AppendLine($"// ===== End: {fileName} =====");

                                filesCopied++;
                                totalSize += fileContent.Length;
                            }
                            catch (IOException ioEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error reading file '{filePath}': {ioEx.Message}");
                                contentBuilder.AppendLine($"// ===== Error reading {Path.GetFileName(filePath)}: {ioEx.Message} =====");
                            }
                            catch (Exception readEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"General error processing file '{filePath}': {readEx.Message}");
                                contentBuilder.AppendLine($"// ===== Error processing {Path.GetFileName(filePath)}: {readEx.Message} =====");
                            }
                        }
                    }
                } // end foreach

                // --- 5. 클립보드에 복사 및 메시지 표시 ---
                if (filesCopied > 0)
                {
                    string finalContent = contentBuilder.ToString();
                    try
                    {
                        Clipboard.SetText(finalContent); // WPF Clipboard 사용
                        string message = $"{filesCopied} C# script file(s) content copied to clipboard (Approx. {totalSize:N0} characters).";
                        VsShellUtilities.ShowMessageBox(this.package, message, "Copy Complete", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    catch (Exception clipEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting clipboard text: {clipEx.Message}");
                        VsShellUtilities.ShowMessageBox(this.package, $"Failed to copy text to clipboard: {clipEx.Message}", "Clipboard Error", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }
                else
                {
                    // 유효한 C# 파일이 선택되지 않았을 경우
                    VsShellUtilities.ShowMessageBox(this.package, "No valid C# script file(s) selected.", "Info", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }
            catch (Exception ex) // DTE 객체 접근 등에서 발생할 수 있는 예외 처리
            {
                System.Diagnostics.Debug.WriteLine($"Error processing selected items: {ex.ToString()}");
                VsShellUtilities.ShowMessageBox(this.package, $"An unexpected error occurred while processing selection: {ex.Message}", "Error", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}