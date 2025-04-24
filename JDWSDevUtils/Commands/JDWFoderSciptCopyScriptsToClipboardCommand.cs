using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell; 
using Microsoft.VisualStudio.Shell.Interop; 
using System;
using System.ComponentModel.Design; 
using System.Globalization; 
using System.IO; 
using System.Text; 
using System.Threading; 
using System.Threading.Tasks; 
using System.Windows; 
using Task = System.Threading.Tasks.Task; 

namespace JDWSDevUtils.Commands
{
    /// <summary>
    /// Command handler for the "Copy Scripts to Clipboard" command.
    /// </summary>
    internal sealed class JDWCopyScriptsToClipboardCommand
    {
        /// <summary>
        /// Command ID for CopyScriptsToClipboard. Must match the IDSymbol in the .vsct file.
        /// </summary>
        public const int CommandId = 0x0101; // As planned: 0x0101

        /// <summary>
        /// Command menu group (command set GUID). Should match the Package GUID.
        /// </summary>
        public static readonly Guid CommandSet = new Guid(JDWSDevUtilsPackage.PackageGuidString);

        /// <summary>
        /// VS Package instance.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="JDWCopyScriptsToClipboardCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the VSCT file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private JDWCopyScriptsToClipboardCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            // Using MenuCommand as this command likely doesn't need dynamic status updates via BeforeQueryStatus
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command. Can be used to get access to the command object.
        /// </summary>
        public static JDWCopyScriptsToClipboardCommand Instance { get; private set; }

        /// <summary>
        /// Gets the IAsyncServiceProvider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => this.package;

        /// <summary>
        /// Initializes the singleton instance of the command asynchronously. Needs to be called from the Package's InitializeAsync.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Ensure we are on the UI thread before calling the constructor which uses the command service
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                Instance = new JDWCopyScriptsToClipboardCommand(package, commandService);
            }
            // Handle case where commandService is null if necessary
        }

        /// <summary>
        /// Executes when the menu item associated with this command is clicked.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            // Ensure we are on the UI thread before accessing DTE or showing message boxes
            ThreadHelper.ThrowIfNotOnUIThread();

            // 1. Get DTE Service
            // Using GetService directly as GetServiceAsync().Result can cause deadlocks if not careful.
            // Ensure InitializeAsync was called on the UI thread.
            var dte = (this.package as IServiceProvider).GetService(typeof(SDTE)) as DTE2;
            if (dte == null)
            {
                VsShellUtilities.ShowMessageBox(this.package, "Cannot get DTE service.", "Error", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            // 2. Get Selected Folder Path
            UIHierarchy solutionExplorer = dte.ToolWindows.SolutionExplorer;
            var selectedItems = (Array)solutionExplorer.SelectedItems;
            if (selectedItems == null || selectedItems.Length == 0) return; // Nothing selected

            UIHierarchyItem selectedHierarchyItem = selectedItems.GetValue(0) as UIHierarchyItem;
            ProjectItem selectedProjectItem = selectedHierarchyItem?.Object as ProjectItem;

            // Ensure it's a physical folder
            if (selectedProjectItem?.Kind != EnvDTE.Constants.vsProjectItemKindPhysicalFolder) return;

            string folderPath = null;
            try
            {
                // Attempt to get the full path property
                folderPath = selectedProjectItem.Properties?.Item("FullPath")?.Value?.ToString();
            }
            catch (ArgumentException) { /* Property might not exist */ }
            catch (Exception ex) // Catch other potential errors getting properties
            {
                System.Diagnostics.Debug.WriteLine($"Error getting folder path: {ex.Message}");
                VsShellUtilities.ShowMessageBox(this.package, $"Could not determine folder path: {ex.Message}", "Error", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }


            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                VsShellUtilities.ShowMessageBox(this.package, "Could not get a valid folder path.", "Warning", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            try
            {
                // 3. Get *.cs Files in the selected folder only
                string[] csFiles = Directory.GetFiles(folderPath, "*.cs", SearchOption.TopDirectoryOnly);

                if (csFiles.Length == 0)
                {
                    VsShellUtilities.ShowMessageBox(this.package, "No .cs files found in the selected folder.", "Info", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                // 4. Read all files and concatenate into a StringBuilder
                StringBuilder contentBuilder = new StringBuilder();
                int filesCopied = 0;
                long totalSize = 0;

                foreach (string filePath in csFiles)
                {
                    try
                    {
                        string fileContent = File.ReadAllText(filePath);
                        string fileName = Path.GetFileName(filePath);

                        // Add separator with filename
                        contentBuilder.AppendLine($"// ===== Start: {fileName} =====");
                        contentBuilder.AppendLine(fileContent);
                        contentBuilder.AppendLine($"// ===== End: {fileName} ====={Environment.NewLine}"); // Add extra newline for spacing

                        filesCopied++;
                        totalSize += fileContent.Length; // Approximate size
                    }
                    catch (IOException ioEx)
                    {
                        // Log file read errors but continue with others
                        System.Diagnostics.Debug.WriteLine($"Error reading file '{filePath}': {ioEx.Message}");
                        contentBuilder.AppendLine($"// ===== Error reading {Path.GetFileName(filePath)}: {ioEx.Message} ====={Environment.NewLine}");
                    }
                    catch (Exception readEx) // Catch other potential errors
                    {
                        System.Diagnostics.Debug.WriteLine($"General error processing file '{filePath}': {readEx.Message}");
                        contentBuilder.AppendLine($"// ===== Error processing {Path.GetFileName(filePath)}: {readEx.Message} ====={Environment.NewLine}");
                    }
                }

                // 5. Copy the final string to the Clipboard
                string finalContent = contentBuilder.ToString();
                if (finalContent.Length > 0 && filesCopied > 0) // Ensure we actually copied something successfully
                {
                    try
                    {
                        // Use WPF Clipboard class (requires PresentationCore reference)
                        // Needs to run on an STA thread, UI thread is typically STA.
                        Clipboard.SetText(finalContent);

                        // 6. Show Confirmation Message (Optional)
                        string message = $"{filesCopied} C# script file(s) copied to clipboard (Approx. {totalSize:N0} characters).";
                        VsShellUtilities.ShowMessageBox(this.package, message, "Copy Complete", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    catch (Exception clipEx) // Catch potential clipboard errors (e.g., clipboard locked by another process)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting clipboard text: {clipEx.Message}");
                        VsShellUtilities.ShowMessageBox(this.package, $"Failed to copy text to clipboard: {clipEx.Message}", "Clipboard Error", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }
                else if (filesCopied == 0) // Handle case where files existed but couldn't be read
                {
                    VsShellUtilities.ShowMessageBox(this.package, "Could not read content from any .cs file in the folder.", "Warning", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
                // No explicit message if finalContent is empty due only to read errors logged above
            }
            catch (UnauthorizedAccessException uaEx) // Catch potential directory access errors
            {
                System.Diagnostics.Debug.WriteLine($"Access denied error: {uaEx.Message}");
                VsShellUtilities.ShowMessageBox(this.package, $"Access denied reading files from folder: {uaEx.Message}", "Access Error", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex) // Catch other general errors
            {
                System.Diagnostics.Debug.WriteLine($"Error getting files or copying content: {ex.ToString()}"); // Log full exception
                VsShellUtilities.ShowMessageBox(this.package, $"An unexpected error occurred: {ex.Message}", "Error", OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}