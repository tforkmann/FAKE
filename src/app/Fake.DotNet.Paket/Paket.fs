/// Contains helper functions and task which allow to inspect, create and publish [NuGet](https://www.nuget.org/) packages with [Paket](http://fsprojects.github.io/Paket/index.html).
[<RequireQualifiedAccess>]
module Fake.DotNet.Paket

open System
open System.IO
open System.Xml.Linq
open System.Text.RegularExpressions
open Fake.IO.Globbing
open Fake.Core
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators

/// Paket pack parameter type
type PaketPackParams =
    { ToolPath : string
      TimeOut : TimeSpan
      Version : string
      SpecificVersions : (string * string) list
      LockDependencies : bool
      ReleaseNotes : string
      BuildConfig : string
      BuildPlatform : string
      TemplateFile : string
      ExcludedTemplates : string list
      WorkingDir : string
      OutputPath : string
      ProjectUrl : string
      Symbols : bool
      IncludeReferencedProjects : bool
      MinimumFromLockFile : bool
      PinProjectReferences : bool }

let private findPaketExecutable () =
    match Tools.tryFindToolFolderInSubPath "paket" with
    | Some folder ->
        folder @@ "paket"
    | None ->
        (Tools.findToolFolderInSubPath "paket.exe" (Directory.GetCurrentDirectory() @@ ".paket")) @@ "paket.exe"

/// Paket pack default parameters
let PaketPackDefaults() : PaketPackParams =
    { ToolPath = findPaketExecutable ()
      TimeOut = TimeSpan.FromMinutes 5.
      Version = null
      SpecificVersions = []
      LockDependencies = false
      ReleaseNotes = null
      BuildConfig = null
      BuildPlatform = null
      TemplateFile = null
      ProjectUrl = null
      ExcludedTemplates = []
      WorkingDir = "."
      OutputPath = "./temp"
      Symbols = false
      IncludeReferencedProjects = false
      MinimumFromLockFile = false
      PinProjectReferences = false }

/// Paket push parameter type
type PaketPushParams =
    { ToolPath : string
      TimeOut : TimeSpan
      PublishUrl : string
      EndPoint : string
      WorkingDir : string
      DegreeOfParallelism : int
      ApiKey : string }

/// Paket push default parameters
let PaketPushDefaults() : PaketPushParams =
    { ToolPath = findPaketExecutable ()
      TimeOut = System.TimeSpan.MaxValue
      PublishUrl = null
      EndPoint =  null
      WorkingDir = "./temp"
      DegreeOfParallelism = 8
      ApiKey = null }

/// Paket restore packages type
type PaketRestoreParams =
    { ToolPath : string
      TimeOut : TimeSpan
      WorkingDir : string
      ForceDownloadOfPackages : bool
      OnlyReferencedFiles: bool
      Group: string
      ReferenceFiles: string list }

/// Paket restore default parameters
let PaketRestoreDefaults() : PaketRestoreParams =
    { ToolPath = findPaketExecutable ()
      TimeOut = System.TimeSpan.MaxValue
      WorkingDir = "."
      ForceDownloadOfPackages = false
      OnlyReferencedFiles = false
      ReferenceFiles = []
      Group = "" }


let inline private startPaket toolPath workDir (info:ProcStartInfo) =
    { info with 
        FileName = toolPath
        WorkingDirectory = workDir }
let inline private withArgs args (info:ProcStartInfo) =
    { info with Arguments = args }

/// Creates a new NuGet package by using Paket pack on all paket.template files in the working directory.
/// ## Parameters
///
///  - `setParams` - Function used to manipulate the default parameters.
let pack setParams =
    let parameters : PaketPackParams = PaketPackDefaults() |> setParams
    use __ = Trace.traceTask "PaketPack" parameters.WorkingDir

    let xmlEncode (notEncodedText : string) =
        if String.IsNullOrWhiteSpace notEncodedText then ""
        else XText(notEncodedText).ToString().Replace("ß", "&szlig;")

    let version = if String.IsNullOrWhiteSpace parameters.Version then "" else " --version " + Process.toParam parameters.Version
    let buildConfig = if String.IsNullOrWhiteSpace parameters.BuildConfig then "" else " --build-config " + Process.toParam parameters.BuildConfig
    let buildPlatform = if String.IsNullOrWhiteSpace parameters.BuildPlatform then "" else " --build-platform " + Process.toParam parameters.BuildPlatform
    let templateFile = if String.IsNullOrWhiteSpace parameters.TemplateFile then "" else " --template " + Process.toParam parameters.TemplateFile
    let lockDependencies = if parameters.LockDependencies then " --lock-dependencies" else ""
    let excludedTemplates = parameters.ExcludedTemplates |> Seq.map (fun t -> " --exclude " + t) |> String.concat " "
    let specificVersions = parameters.SpecificVersions |> Seq.map (fun (id,v) -> sprintf " --specific-version %s %s" id v) |> String.concat " "
    let releaseNotes = if String.IsNullOrWhiteSpace parameters.ReleaseNotes then "" else " --release-notes " + Process.toParam (xmlEncode parameters.ReleaseNotes)
    let minimumFromLockFile = if parameters.MinimumFromLockFile then " --minimum-from-lock-file" else ""
    let pinProjectReferences = if parameters.PinProjectReferences then " --pin-project-references" else ""
    let symbols = if parameters.Symbols then " --symbols" else ""
    let includeReferencedProjects = if parameters.IncludeReferencedProjects then " --include-referenced-projects" else ""
    let projectUrl = if String.IsNullOrWhiteSpace parameters.ProjectUrl then "" else " --project-url " + Process.toParam parameters.ProjectUrl

    let packResult =
        let cmdArgs =
            sprintf "%s%s%s%s%s%s%s%s%s%s%s%s%s"
                version specificVersions releaseNotes buildConfig buildPlatform templateFile lockDependencies excludedTemplates
                symbols includeReferencedProjects minimumFromLockFile pinProjectReferences projectUrl
        Process.execSimple 
            (startPaket parameters.ToolPath parameters.WorkingDir
                >> withArgs (sprintf "pack \"%s\" %s" parameters.OutputPath cmdArgs)
                >> Process.withFramework)
            parameters.TimeOut

    if packResult <> 0 then failwithf "Error during packing %s." parameters.WorkingDir
    __.MarkSuccess()

/// Pushes the given NuGet packages to the server by using Paket push.
/// ## Parameters
///
///  - `setParams` - Function used to manipulate the default parameters.
///  - `files` - The files to be pushed to the server.
let pushFiles setParams files =
    let parameters : PaketPushParams = PaketPushDefaults() |> setParams

    TraceSecrets.register parameters.ApiKey "<PaketApiKey>"
    match Environment.environVarOrNone "nugetkey" with
    | Some k -> TraceSecrets.register k "<PaketApiKey>"
    | None -> ()
    match Environment.environVarOrNone "nuget-key" with
    | Some k -> TraceSecrets.register k "<PaketApiKey>"
    | None -> ()
    
    let packages = Seq.toList files
    let url = if String.IsNullOrWhiteSpace parameters.PublishUrl then "" else " --url " + Process.toParam parameters.PublishUrl
    let endpoint = if String.IsNullOrWhiteSpace parameters.EndPoint then "" else " --endpoint " + Process.toParam parameters.EndPoint
    let key = if String.IsNullOrWhiteSpace parameters.ApiKey then "" else " --api-key " + Process.toParam parameters.ApiKey

    use __ = Trace.traceTask "PaketPush" (String.separated ", " packages)

    if parameters.DegreeOfParallelism > 0 then
        /// Returns a sequence that yields chunks of length n.
        /// Each chunk is returned as a list.
        let split length (xs: seq<'T>) =
            let rec loop xs =
                [
                    yield Seq.truncate length xs |> Seq.toList
                    match Seq.length xs <= length with
                    | false -> yield! loop (Seq.skip length xs)
                    | true -> ()
                ]
            loop xs

        for chunk in split parameters.DegreeOfParallelism packages do
            let tasks =
                chunk
                |> Seq.toArray
                |> Array.map (fun package -> async {
                        let pushResult =
                            Process.execSimple
                                (startPaket parameters.ToolPath parameters.WorkingDir
                                    >> withArgs (sprintf "push %s%s%s%s" url endpoint key (Process.toParam package))
                                    >> Process.withFramework)
                                parameters.TimeOut
                        if pushResult <> 0 then failwithf "Error during pushing %s." package })

            Async.Parallel tasks
            |> Async.RunSynchronously
            |> ignore

    else
        for package in packages do
            let pushResult =
                Process.execSimple
                    (startPaket parameters.ToolPath parameters.WorkingDir
                        >> withArgs (sprintf "push %s%s%s%s" url endpoint key (Process.toParam package))
                        >> Process.withFramework)
                    parameters.TimeOut
            if pushResult <> 0 then failwithf "Error during pushing %s." package
    __.MarkSuccess()

/// Pushes all NuGet packages in the working dir to the server by using Paket push.
/// ## Parameters
///
///  - `setParams` - Function used to manipulate the default parameters.
let push setParams =
    let parameters : PaketPushParams = PaketPushDefaults() |> setParams

    !! (parameters.WorkingDir @@ "/**/*.nupkg")
    |> pushFiles (fun _ -> parameters)

/// Returns the dependencies from specified paket.references file
let getDependenciesForReferencesFile (referencesFile:string) =
    let getReferenceFilePackages =
        let isSingleFile (line: string) = line.StartsWith "File:"
        let isGroupLine (line: string) = line.StartsWith "group "
        let notEmpty (line: string) = not <| String.IsNullOrWhiteSpace line
        let parsePackageName (line: string) =
            let parts = line.Split(' ')
            parts.[0]
        File.ReadAllLines
        >> Array.filter notEmpty
        >> Array.map (fun s -> s.Trim())
        >> Array.filter (isSingleFile >> not)
        >> Array.filter (isGroupLine >> not)
        >> Array.map parsePackageName

    let getLockFilePackages =
        let getPaketLockFile referencesFile =
            let rec find dir =
                let fi = FileInfo(dir </> "paket.lock")
                if fi.Exists then fi.FullName else find fi.Directory.Parent.FullName
            find <| FileInfo(referencesFile).Directory.FullName

        let breakInParts (line : string) = match Regex.Match(line,"^[ ]{4}([^ ].+) \((.+)\)") with
                                           | m when m.Success && m.Groups.Count = 3 -> Some (m.Groups.[1].Value, m.Groups.[2].Value)
                                           | _ -> None

        getPaketLockFile
        >> File.ReadAllLines
        >> Array.choose breakInParts

    let refLines = getReferenceFilePackages referencesFile

    getLockFilePackages referencesFile
    |> Array.filter (fun (n, _) -> refLines |> Array.exists (fun pn -> pn.Equals(n, StringComparison.OrdinalIgnoreCase)))

/// Restores all packages referenced in either a paket.dependencies or a paket.references file using Paket
/// ## Parameters
///
///  - `setParams` - Function used to manipulate the default parameters.
let restore setParams =
    let parameters : PaketRestoreParams = PaketRestoreDefaults() |> setParams
    let forceRestore = if parameters.ForceDownloadOfPackages then " --force " else ""
    let onlyReferenced = if parameters.OnlyReferencedFiles then " --only-referenced " else ""
    let groupArg = if parameters.Group <> "" then (sprintf " --group %s " parameters.Group) else ""
    let referencedFiles =
        if parameters.ReferenceFiles |> List.isEmpty |> not
        then (sprintf " --references-files %s " (System.String.Join(" ", parameters.ReferenceFiles)))
        else ""

    use __ = Trace.traceTask "PaketRestore" parameters.WorkingDir

    let restoreResult =
        Process.execSimple
            (startPaket parameters.ToolPath parameters.WorkingDir
                >> withArgs (sprintf "restore %s%s%s%s" forceRestore onlyReferenced groupArg referencedFiles)
                >> Process.withFramework)
            parameters.TimeOut

    if restoreResult <> 0 then failwithf "Error during restore %s." parameters.WorkingDir
    __.MarkSuccess()
