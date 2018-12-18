module Fake.Core.GlobbingTests

open System
open System.IO
open System.Reflection
open Fake.IO
open Fake.IO.Globbing
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Expecto

let getFileIncludeWithKnownBaseDir includes : LazyGlobbingPattern=
    { Fake.IO.Globbing.LazyGlobbingPattern.BaseDirectory = @"C:\Project"
      Fake.IO.Globbing.LazyGlobbingPattern.Includes = includes
      Fake.IO.Globbing.LazyGlobbingPattern.Excludes = [] } 

[<Tests>]
let tests = 
  testList "Fake.Core.Globbing.Tests" [
    testCase "Test IsMatch works on relative Paths - #1029" <| fun _ ->
      let globExe = // !! "folder/*.exe"
          { Fake.IO.Globbing.ResolvedGlobbingPattern.BaseDirectory = Path.GetFullPath "."
            Fake.IO.Globbing.ResolvedGlobbingPattern.Includes = [ "folder/*.exe" ]
            Fake.IO.Globbing.ResolvedGlobbingPattern.Excludes = []
            Fake.IO.Globbing.ResolvedGlobbingPattern.Results = 
              [ "folder/file1.exe"
                "folder/file2.exe" ] }
      Expect.equal true (globExe.IsMatch "folder/test.exe") "Glob should match relative paths"
      Expect.equal true (globExe.IsMatch (Path.GetFullPath "folder/test.exe")) "Glob should match full paths"
    testCase "It should resolve multiple directories" <| fun _ ->
        let fileIncludes = getFileIncludeWithKnownBaseDir [@"test1\bin\*.dll"; @"test2\bin\*.dll"]
        let dirIncludes = GlobbingPattern.getBaseDirectoryIncludes(fileIncludes)
        Expect.equal dirIncludes.Length 2 "Should have 2 dirs"
        Expect.contains dirIncludes (Glob.normalizePath(@"C:\Project\test1\bin"))  "Should contain first folder"
        Expect.contains dirIncludes (Glob.normalizePath(@"C:\Project\test2\bin")) "Should contain second folder"

    testCase "should only take the most root path when multiple directories share a root" <| fun _ ->
        let fileIncludes = getFileIncludeWithKnownBaseDir [@"tests\**\test1\bin\*.dll"; @"tests\test2\bin\*.dll"]
        let dirIncludes = GlobbingPattern.getBaseDirectoryIncludes(fileIncludes)
        Expect.equal dirIncludes.Length 1 "Should have only 1 directory"
        Expect.contains  dirIncludes (Glob.normalizePath(@"C:\Project\tests")) "Should contain tests folder"

    testCase "glob should handle substring directories properly" <| fun _ ->
        let testDir = Path.GetTempFileName()
        File.Delete testDir
        Directory.CreateDirectory testDir |> ignore
        try
          let name = testDir </> "Name"
          let nameWithSuffix = testDir </> "NameWithSuffix"
          Directory.CreateDirectory name |> ignore
          Directory.CreateDirectory nameWithSuffix |> ignore
          File.WriteAllText(nameWithSuffix </> "match1.txt", "match1")
          File.WriteAllText(nameWithSuffix </> "match2.txt", "match2")
          File.WriteAllText(nameWithSuffix </> "match3.txt", "match3")

          !! (nameWithSuffix </> "match*.txt")
          |> GlobbingPattern.setBaseDir name
          |> Seq.map (fun f -> Path.GetFileName f)
          |> Seq.sort
          |> Seq.toList
          |> Flip.Expect.equal "Expected equal lists." ["match1.txt"; "match2.txt"; "match3.txt"]
        finally
          Directory.Delete(testDir, true)
  ]

[<Tests>]
let toolsTests = 
  let currentDir =
    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
  let createUniqueDirectory () =
      let folder = currentDir </> ((Guid.NewGuid ()).ToString())
      Directory.CreateDirectory folder |> ignore
      folder
  
  // Sequenced because otherwise folders conflict with the globbing pattern '!! "./**"' in the impl.
  testSequencedGroup "Find tool paths" <|
    testList "Fake.Core.Globbing.Tools.Tests" [
      testCase "Test try find tool folder in sub path" <| fun _ ->
        let folder = createUniqueDirectory ()

        try
          let filepath = folder </> "sometool"
          File.create filepath |> ignore

          Tools.tryFindToolFolderInSubPath "sometool"
          |> Flip.Expect.equal "Expected tools folder to be found" (Some folder)
        finally
          Directory.Delete(folder, true)

      testCase "Test cannot find tool folder in sub path" <| fun _ ->
        Tools.tryFindToolFolderInSubPath "SomeMissingTool"
        |> Flip.Expect.isNone "Expected tools folder not to be found"

      testCase "Test find tool folder in sub path" <| fun _ ->
        let folder = createUniqueDirectory ()

        try
          let filepath = folder </> "sometool"
          File.create filepath |> ignore

          Tools.findToolFolderInSubPath "sometool" "defaultToolsPath"
          |> Flip.Expect.equal "Expected tools folder to be found" folder
        finally
          Directory.Delete(folder, true)

      testCase "Test cannot find tool folder in sub path returns default" <| fun _ ->
        Tools.findToolFolderInSubPath "SomeMissingTool" "defaultpath"
        |> Flip.Expect.equal "Expected default path to be returned" "defaultpath"
    ]
