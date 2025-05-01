using EnvDTE; // Visual Studio 자동화 모델 (DTE) 사용
using EnvDTE80; // DTE의 확장 버전 사용
using Microsoft.CodeAnalysis; // Roslyn 기본 API
using Microsoft.CodeAnalysis.CSharp; // C# 관련 Roslyn API (SyntaxFactory 등)
using Microsoft.CodeAnalysis.CSharp.Syntax; // C# 구문 노드 타입 (VariableDeclarationSyntax 등)
using Microsoft.CodeAnalysis.MSBuild; // MSBuild 기반 워크스페이스 로딩
using Microsoft.CodeAnalysis.Simplification; // Simplifier 사용
using Microsoft.CodeAnalysis.Text; // SourceText 등 텍스트 관련 API
using Microsoft.VisualStudio.Shell; // Visual Studio SDK 기본 클래스 (AsyncPackage 등)
using Microsoft.VisualStudio.Shell.Interop; // Visual Studio COM 인터페이스 (IVsShell 등)
using System;
using System.Collections.Generic; // Dictionary 사용
using System.ComponentModel.Design; // MenuCommand, CommandID 사용
using System.Diagnostics; // Debug.WriteLine, Stopwatch 사용
using System.IO; // File, Path 클래스 사용
using System.Linq; // LINQ 쿼리 사용
using System.Threading; // CancellationToken 사용
using System.Threading.Tasks; // 비동기 프로그래밍 (Task)
// 네임스페이스 충돌을 피하기 위한 별칭(Alias) 설정
using RoslynProject = Microsoft.CodeAnalysis.Project; // EnvDTE.Project와 구분
using RoslynDocument = Microsoft.CodeAnalysis.Document; // EnvDTE.Document와 구분
using Task = System.Threading.Tasks.Task; // System.Threading.Tasks.Task 명시

namespace JDWSDevUtils.Commands
{
    /// <summary>
    /// Visual Studio 편집기에서 현재 열려있는 C# 문서 내의 'var' 키워드를
    /// 컴파일러가 추론한 명시적 타입으로 치환하고, 타입 이름을 간소화하는 VSIX 명령 클래스.
    /// Roslyn 분석기를 사용하여 타입 추론, 코드 변환 및 이름 간소화를 수행함.
    /// </summary>
    internal sealed class JDWConvertVarCommand
    {
        // --- 상수 및 필드 ---
        /// <summary>명령 ID (.vsct 파일과 일치)</summary>
        public const int CommandId = 0x0300;
        /// <summary>명령 그룹 GUID (.vsct 파일과 일치)</summary>
        public static readonly Guid CommandSet = new Guid(JDWSDevUtilsPackage.PackageGuidString);
        /// <summary>호스팅 VSPackage 참조</summary>
        private readonly AsyncPackage package;

        // --- 싱글톤 인스턴스 및 초기화 ---
        /// <summary>싱글톤 인스턴스</summary>
        public static JDWConvertVarCommand Instance { get; private set; }
        /// <summary>VS 서비스 접근용 프로바이더</summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => this.package;

        /// <summary>Private 생성자 (싱글톤, 명령 등록)</summary>
        private JDWConvertVarCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.ExecuteHandler, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>비동기 초기화 (패키지에서 호출)</summary>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                Instance = new JDWConvertVarCommand(package, commandService);
            }
        }

        // --- 명령 실행 로직 ---

        /// <summary>메뉴 클릭 이벤트 핸들러</summary>
        private void ExecuteHandler(object sender, EventArgs e)
        {
            // 실제 작업은 비동기 메서드에서 수행 (Fire and forget)
            _ = ExecuteAsync(sender, e);
        }

        /// <summary>'var' 변환 비동기 작업</summary>
        private async Task ExecuteAsync(object sender, EventArgs e)
        {
            Stopwatch stopwatch = Stopwatch.StartNew(); // 성능 측정 시작
            long elapsedWorkspace = 0, elapsedAnalysis = 0, elapsedSimplify = 0; // 단계별 시간 측정 변수

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // UI 스레드 시작

            // DTE 서비스 가져오기
            DTE2 dte = await ServiceProvider.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte == null)
            {
                ShowMessageBox("Error", "Visual Studio DTE service is not available.", OLEMSGICON.OLEMSGICON_CRITICAL);
                return;
            }
            // 활성 문서 가져오기
            EnvDTE.Document activeDocument = dte.ActiveDocument;
            if (activeDocument == null)
            {
                ShowMessageBox("Warning", "No active document is open.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }
            // 활성 문서 경로 및 유효성 검사
            string filePath = activeDocument.FullName;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || Path.GetExtension(filePath)?.ToLowerInvariant() != ".cs")
            {
                ShowMessageBox("Warning", "The active document is not a valid C# file.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            // --- 자기 자신 실행 방지 ---
            // 명령 소스 코드 파일 자체에 대해 실행되는 것을 방지함 (사용자 실수 방지)
            // 실제 수정하려는 대상 파일을 활성화한 상태에서 명령을 실행해야 함.
            if (Path.GetFileName(filePath).Equals("JDWConvertVarCommand.cs", StringComparison.OrdinalIgnoreCase))
            {
                ShowMessageBox("Warning", "This command cannot be run on its own source code file.\nPlease run it on the target C# file you want to modify.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }
            // --- 자기 자신 실행 방지 끝 ---

            // 프로젝트 경로 가져오기 및 유효성 검사
            EnvDTE.Project containingProject = activeDocument.ProjectItem?.ContainingProject;
            if (containingProject == null)
            {
                ShowMessageBox("Error", "Could not find the project containing the active document.", OLEMSGICON.OLEMSGICON_CRITICAL);
                return;
            }
            string projectPath = containingProject.FullName;
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
            {
                ShowMessageBox("Error", "Could not find the project file path (.csproj).", OLEMSGICON.OLEMSGICON_CRITICAL);
                return;
            }

            // Roslyn 작업 시작 (try-catch)
            try
            {
                // --- 성능 병목 구간 시작: 워크스페이스 생성 및 프로젝트 로딩 ---
                // MSBuildWorkspace 사용 (성능 개선 필요 시 VisualStudioWorkspace 고려)
                using (var workspace = MSBuildWorkspace.Create())
                {
                    workspace.WorkspaceFailed += (o, args) => { Debug.WriteLine($"Roslyn Workspace loading failed: {args.Diagnostic}"); };

                    // 프로젝트 로딩 (시간 소요 지점 1)
                    RoslynProject loadedProject = await workspace.OpenProjectAsync(projectPath);
                    elapsedWorkspace = stopwatch.ElapsedMilliseconds;

                    // Roslyn 문서 객체 가져오기
                    DocumentId documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
                    if (documentId == null) { ShowMessageBox("Error", "Could not find the document in the Roslyn workspace.", OLEMSGICON.OLEMSGICON_CRITICAL); return; }
                    RoslynDocument document = workspace.CurrentSolution.GetDocument(documentId);
                    if (document == null) { ShowMessageBox("Error", "Failed to get the document object from the workspace.", OLEMSGICON.OLEMSGICON_CRITICAL); return; }

                    // --- 분석 단계 ---
                    // 시맨틱 모델 및 구문 트리 얻기 (시간 소요 지점 2)
                    SemanticModel semanticModel = await document.GetSemanticModelAsync();
                    SyntaxNode syntaxRoot = await document.GetSyntaxRootAsync();
                    if (semanticModel == null || syntaxRoot == null) { ShowMessageBox("Error", "Could not get semantic model or syntax root.", OLEMSGICON.OLEMSGICON_CRITICAL); return; }
                    elapsedAnalysis = stopwatch.ElapsedMilliseconds - elapsedWorkspace;

                    // 'var' 선언 찾기 및 변환 목록 생성
                    Dictionary<SyntaxNode, SyntaxNode> nodesToReplace = new Dictionary<SyntaxNode, SyntaxNode>(); 
                    //LINQ 쿼리 결과 타입은 장황해서 var가 일반적
                    var varDeclarations = syntaxRoot.DescendantNodes().OfType<VariableDeclarationSyntax>().Where(vds => vds.Type.IsVar);

                    foreach (var decl in varDeclarations)
                    {
                        TypeSyntax varTypeNode = decl.Type;
                        ITypeSymbol? inferredType = semanticModel.GetTypeInfo(varTypeNode).ConvertedType;

                        // 변환 제외 조건: null, 오류 타입, 익명 타입, 또는 여전히 "var"인 경우, 익명타입은 절대 명시적 타입으로 바뀌지 않고, 그대로 var로 남는다.
                        if (inferredType == null || inferredType.TypeKind == TypeKind.Error || inferredType.IsAnonymousType || inferredType.ToDisplayString() == "var")
                        {
                            continue;
                        }

                        try
                        {
                            // 완전 정규화된 이름으로 TypeSyntax 생성 (안전성 우선)
                            string explicitTypeName = inferredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            TypeSyntax explicitTypeSyntax = SyntaxFactory
                                .ParseTypeName(explicitTypeName)
                                .WithTriviaFrom(varTypeNode)
                                .WithAdditionalAnnotations(Simplifier.Annotation); nodesToReplace.Add(varTypeNode, explicitTypeSyntax);
                        }
                        catch (Exception ex) { Debug.WriteLine($"Error parsing type name '{inferredType.ToDisplayString()}': {ex.Message}"); continue; }
                    }

                    // 변환할 내용이 있을 경우 진행
                    if (nodesToReplace.Any())
                    {
                        // 1단계: ReplaceNodes로 일괄 치환 (Immutable 처리)
                        SyntaxNode newRoot = syntaxRoot.ReplaceNodes(nodesToReplace.Keys, (originalNode, _) => nodesToReplace[originalNode]);

                        // --- 간소화 단계 --- (시간 소요 지점 3)
                        // 2단계: Simplifier로 이름 간소화 및 using 정리 시도
                        RoslynDocument tempDocument = document.WithSyntaxRoot(newRoot);
                        RoslynDocument simplifiedDocument = await Simplifier.ReduceAsync(tempDocument, Simplifier.Annotation, cancellationToken: CancellationToken.None);
                        SyntaxNode simplifiedRoot = await simplifiedDocument.GetSyntaxRootAsync();
                        SourceText finalSourceText = await simplifiedDocument.GetTextAsync();
                        elapsedSimplify = stopwatch.ElapsedMilliseconds - elapsedWorkspace - elapsedAnalysis;

                        // 파일 쓰기 (VS 버퍼 직접 수정 방식 고려 가능)
                        File.WriteAllText(filePath, finalSourceText.ToString());

                        // 최종 결과 메시지 (성능 포함)
                        stopwatch.Stop();
                        ShowMessageBox("Success", $"{nodesToReplace.Count} " +
                            $"'var' declarations replaced.\nElapsed: {stopwatch.ElapsedMilliseconds}ms", OLEMSGICON.OLEMSGICON_INFO);

                    }
                    else // 변환할 내용 없음
                    {
                        stopwatch.Stop();
                        ShowMessageBox("Information", "No replaceable 'var' declarations were found.", OLEMSGICON.OLEMSGICON_INFO);
                    }
                } // using (MSBuildWorkspace)
            }
            catch (Exception ex) // 전체 작업 예외 처리
            {
                stopwatch.Stop();
                Debug.WriteLine($"Error converting var to explicit type: {ex}");
                ShowMessageBox("Error", $"An unexpected error occurred: {ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
            }
        }

        /// <summary>메시지 박스 표시 헬퍼</summary>
        private void ShowMessageBox(string title, string message, OLEMSGICON icon)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // UI 스레드 확인
            VsShellUtilities.ShowMessageBox(this.package, message, title, icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
