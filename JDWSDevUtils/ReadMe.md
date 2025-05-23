
## ✅ 현재 구현된 기능

### 1. 폴더 내 스크립트 일괄 열기
- Solution Explorer에서 폴더 우클릭 → `[DevUtils] Open All Scripts in Folder`
- `.cs` 파일만 탐색하여 Visual Studio에서 모두 열어줌
- 모든 스크립트는 부동(Floating) 창으로 열림

### 2. 폴더 내 스크립트 클립보드 복사
- Solution Explorer에서 폴더 우클릭 → `[DevUtils] Copy All Folder Scripts to Clipboard`
- `.cs` 파일의 모든 내용을 읽어 문자열로 합친 후 클립보드에 복사
- 파일명 별 구분선 주석 삽입 지원

### 3. 단일 스크립트 클립보드 복사
- 파일 우클릭 → `[DevUtils] Copy Script Content`
- 하나의 스크립트 파일만 클립보드에 복사됨
### 4. 단일 스크립트 내 `var` 키워드 명시적 타입으로 변환

- 코드 파일 열고 내부 우클릭 → `[DevUtils] Convert 'var' to Explicit Type`
    
- Roslyn SemanticModel 기반으로 `var` → 추론 타입으로 치환
    
- FullyQualified 형식으로 치환하여 using 누락 문제 없음
    
- `익명 타입`, `오류 타입` 자동 필터링됨
    
- 현재 파일을 직접 덮어쓰기 (VS Undo 통합은 아님)

## 🚧 추가 예정 기능 목록

### 1. 참조 관계 열기 기능
- 특정 스크립트를 기준으로 참조/참조된 파일을 전부 열기 (난이도 높음)
- Roslyn 기반의 정적 분석을 활용할 예정

### 2. 멀티 언어 확장 (Python, JS 등)
- `.py`, `.js`, `.ts`, `.cpp` 등 다양한 언어에 대한 폴더 열기 및 복사 기능 확장
- 파일 확장자 필터를 선택 가능한 방식으로 고려

### 3. 다국어 지원
- 현재 Resource 파일을 사용 중이나, 메뉴 지역화 실패
- Visual Studio 자체 로케일 언어 리소스 로딩 방식 분석 필요

### 4. 폴더 내 모든 스크립트의 `var` 일괄 치환

- 폴더 우클릭 → `[DevUtils] Replace All 'var' in Folder Scripts`
    
- 모든 `.cs` 파일 순회하며 Roslyn 분석 후 `var` → 명시적 타입 치환
    
- 단일 파일 처리 방식과 동일한 로직 재사용 예정


##  🚀유틸성 도구 확장 방향

| 모듈명        | 기능 설명                          |
| ---------- | ------------------------------ |
| 코드 자동화 도우미 | 반복 패턴 생성기, Getter/Setter 자동화 등 |
| 주석 자동 생성   | 요약 주석 + 파라미터 주석 자동 삽입          |
| 코드 스니펫 도우미 | 자주 쓰는 코드 패턴 저장/삽입              |
| 템플릿 기반 생성기 | Unity 모듈 템플릿, MVC 등 구조 자동 생성   |
| 파일 구조 정리기  | 폴더 정리, 네이밍 변경 자동화              |

## ⚠️ 이슈 및 해결 내역
- 메뉴가 전혀 뜨지 않던 원인 → **코드 문제 아님**
-  Visual Studio 확장 프로젝트를 새로 생성하니 해결됨 (설정 깨졌던 문제로 추정)
- 일부 한글 깨짐 → UTF-8 인코딩을 명시적으로 지정함

##  명령어 목록

| 커맨드 이름                        | ID       | 설명                            |
| ----------------------------- | -------- | ----------------------------- |
| `OpenScriptsInFolder`         | `0x0100` | 현재 폴더에서 스크립트 열기               |
| `CopyScriptsToClipboard`      | `0x0101` | 스크립트 전체 클립보드 복사               |
| `OpenReferencedScripts`       | `0x0102` | 참조 스크립트 자동 열기                 |
| `ConfigureScriptExtensions`   | `0x0103` | 필터링 확장자 설정창 열기                |
| `CopySingleScriptToClipboard` | `0x0200` | 선택된 단일 스크립트 복사 기능             |
| `ConvertVartoExplicitType`    | `0x0300` | 선택된 단일 스크립트 var를 명시적 타입으로 교체함 |

##  명령어 구분 기준

|구분|ID Prefix|Context|설명|
|---|---|---|---|
|**01**|`0x01XX`|**폴더 우클릭**|`.cs` 파일 전체 처리 (예: 열기, 복사 등)|
|**02**|`0x02XX`|**단일 스크립트 우클릭**|선택한 `.cs` 파일 하나에 대해 동작 수행|
|**03**|`0x03XX`|**스크립트 내부 코드 우클릭**|코드 내부에서 직접 동작 수행 (예: var 변환 등)|
