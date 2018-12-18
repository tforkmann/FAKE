# Getting started

In this tutorial you will learn how to set up a complete build infrastructure with FAKE. This includes:

* how to install the latest FAKE version
* how to edit and run scripts
* how to automatically compile your C# or F# projects
* how to automatically run NUnit tests on your projects

## Install FAKE

FAKE is completely written in F# and all build scripts will also be written in F#, but this doesn't imply that you have to learn programming in F#. In fact the FAKE syntax is hopefully very easy to learn.

There are various ways to install FAKE 5:

* Install FAKE as a global dotnet tool:
    * To install FAKE globally, run:
        <pre><code class="lang-bash">
        dotnet tool install fake-cli -g
        </code></pre>
    * To install FAKE into `your_tool_path`, run:
        <pre><code class="lang-bash">
        dotnet tool install fake-cli --tool-path your_tool_path
        </code></pre>

    Use `--version` to specify the version of FAKE. See the [`global_tool`](https://github.com/FakeBuild/fake-bootstrap/tree/global_tool) branch of `fake-bootstrap` for ideas to bootstrap in your CI process.

* Bootstrap via the `fake dotnet new` [template](fake-template.html). The template bootstraps FAKE and sets up a basic build-script.
    * To install the template run:
        <pre><code class="lang-bash">
        dotnet new -i "fake-template::*"
        </code></pre>
    * Then run the template with:
        <pre><code class="lang-bash">
        dotnet new fake
        </code></pre>

    See the [template](fake-template.html) page for more information.

* Install the 'fake' or 'fake-netcore' package for your system (currenty chocolatey).
  Example `choco install fake`

* Use it as a dotnet tool: Add `<DotNetCliToolReference Include="dotnet-fake" Version="5.*" />` to your dependencies and run `dotnet fake ...` instead of `fake ...`, see [this example](https://github.com/FakeBuild/fake-bootstrap/blob/master/dotnet-fake.csproj)


* Bootstrap via a shell script (fake.cmd/fake.sh),
  see this [example project](https://github.com/FakeBuild/fake-bootstrap)
    <div class="alert alert-warning">
        <h5>WARNING</h5>
        <p>These scripts have no versioning story. You either need to take care of versions yourself (and lock them) or your builds might break on major releases.</p>
    </div>

* Bootstrap via paket `clitool`, this is basically the same as `DotNetCliToolReference` but managed via paket. See the [`paket_clitool`](https://github.com/FakeBuild/fake-bootstrap/tree/paket_clitool) branch of `fake-bootstrap` in particular the [build.proj](https://github.com/FakeBuild/fake-bootstrap/blob/paket_clitool/build.proj) file.

## One note on Intellisense

Whenever you update the dependencies (part of the example), you need to delete the `<script>.fsx.lock` file and re-run fake to update all files and intellisense!

## Example - Compiling and building your .NET application

This example will guide you by adding a fake script to your existing .NET application.

### Getting started

Initially we need to create a file called `build.fsx` where all our build-logic will reside.
Create a new file with Visual Studio or Visual Studio Code (with ionide) and paste the following content:

```fsharp
#r "paket:
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"
```

This is all we need for now to declare that we need the `Fake.Core.Target` module and want to enable intellisense.

Now run `fake run build.fsx` to make fake prepare our environment. Now our IDE can load the dependencies and we will have intellisense enabled (you might need to reopen the script file on some editors).

Now that we have setup our basic environment to edit the script file we can add our first target:

```fsharp
#r "paket:
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core

// Default target
Target.create "Default" (fun _ ->
  Trace.trace "Hello World from FAKE"
)

// start build
Target.runOrDefault "Default"
```

As you can see the code is really simple. The first few lines (`nuget Fake.Core.Target` and `open Fake.Core`) load the fake modules we need and this is vital for all build scripts to support creating and running targets. The `#load` line is optional but a good way to make the IDE aware of all the modules (for intellisense and IDE support)

After this header the *Default* target is defined. A target definition contains two important parts. The first is the name of the target (here "Default") and the second is an action (here a simple trace of "Hello world").

The last line runs the "Default" target - which means it executes the defined action of the target.

Try running your new target via `fake run build.fsx` or the shortcut for a file called `build.fsx`: `fake build`

### Cleaning the last build output

A typical first step in most build scenarios is to clean the output of the last build. We can achieve this in two steps:

First change your header to the following by adding the `Fake.IO.FileSystem` module:

```fsharp
#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"
```

Now we need to remove the `build.fsx.lock` file and run `fake build` in order to restore the newly added `Fake.IO.FileSystem` module.

Since we now can rely on intellisense we can easily discover the various modules and functions in `Fake.IO`, for example the `Shell` module provides various functions you expect from regular shell scripting, we will use `Shell.cleanDir` which will ensure the given directory is empty by deleting everything within or creating the directory if required:


```fsharp
#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.IO

// Properties
let buildDir = "./build/"

// Targets
Target.create "Clean" (fun _ ->
  Shell.cleanDir buildDir
)

Target.create "Default" (fun _ ->
  Trace.trace "Hello World from FAKE"
)

// Dependencies
open Fake.Core.TargetOperators

"Clean"
  ==> "Default"

// start build
Target.runOrDefault "Default"
```
<div class="alert alert-info">
    <h5>HINT</h5>
    You can explore the APIs for example by writing <code>Fake.IO.</code> and waiting for intellisense (or pressing <code>Ctrl+Space</code>).
    You can remove <code>Fake.IO</code> once you put <code>open Fake.IO</code> on top.
</div>

We introduced some new concepts in this snippet. At first we defined a global property called `buildDir` with the relative path of a temporary build folder.

In the `Clean` target we use the `Shell.cleanDir` task to clean up this build directory. As explained above this simply deletes all files in the folder or creates the directory if necessary.

In the dependencies section we say that the *Default* target has a dependency on the *Clean* target. In other words *Clean* is a prerequisite of *Default* and will run before the *Default* target is executed:

![alt text](pics/gettingstarted/afterclean.png "We introduced a Clean target")

### Compiling the application

In the next step we want to compile our C# libraries, which means we want to compile all csproj-files under */src/app* with MSBuild.

Again we need a new module for this, namely `Fake.DotNet.MSBuild`.

Just like before add the required module on top via `nuget Fake.DotNet.MSBuild`, delete the `build.fsx.lock` file and run the script.
Now edit the script so it looks like this:

```fsharp
#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators //enables !! and globbing
open Fake.DotNet
open Fake.Core

// Properties
let buildDir = "./build/"

// Targets
Target.create "Clean" (fun _ ->
  Shell.cleanDir buildDir
)

Target.create "BuildApp" (fun _ ->
  !! "src/app/**/*.csproj"
    |> MSBuild.runRelease id buildDir "Build"
    |> Trace.logItems "AppBuild-Output: "
)

Target.create "Default" (fun _ ->
  Trace.trace "Hello World from FAKE"
)

open Fake.Core.TargetOperators

"Clean"
  ==> "BuildApp"
  ==> "Default"

// start build
Target.runOrDefault "Default"
```

We defined a new build target named "BuildApp" which compiles all csproj-files with the MSBuild task and the build output will be copied to `buildDir`.

In order to find the right project files FAKE scans the folder *src/app/* and all subfolders with the given pattern (the `!!` operator was imported from `Fake.IO.FileSystem` via `open Fake.IO.Globbing.Operators`). Therefore a similar FileSet definition like in NAnt or MSBuild (see [project page](https://github.com/fsharp/FAKE) for details) is used.

In addition the target dependencies are extended again. Now *Default* is dependent on *BuildApp* and *BuildApp* needs *Clean* as a prerequisite.

This means the execution order is: Clean ==> BuildApp ==> Default.

![alt text](pics/gettingstarted/aftercompile.png "We introduced a Build target")

### Compiling test projects

Now our main application will be built automatically and it's time to build the test project. We use the same concepts as before:

```fsharp
#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.DotNet
open Fake.Core


// Properties
let buildDir = "./build/"
let testDir  = "./test/"

// Targets
Target.create "Clean" (fun _ ->
  Shell.cleanDirs [buildDir; testDir]
)

Target.create "BuildApp" (fun _ ->
    !! "src/app/**/*.csproj"
    |> MSBuild.runRelease id buildDir "Build"
    |> Trace.logItems "AppBuild-Output: "
)

Target.create "BuildTest" (fun _ ->
  !! "src/test/**/*.csproj"
    |> MSBuild.runDebug id testDir "Build"
    |> Trace.logItems "TestBuild-Output: "
)

Target.create "Default" (fun _ ->
  Trace.trace "Hello World from FAKE"
)

open Fake.Core.TargetOperators
"Clean"
  ==> "BuildApp"
  ==> "BuildTest"
  ==> "Default"

// start build
Target.runOrDefault "Default"
```

This time we defined a new target "BuildTest" which compiles all C# projects below *src/test/* in Debug mode and we put the target into our build order.

### Running the tests with NUnit

Now all our projects will be compiled and we can use FAKE's NUnit task in order to let NUnit test our assembly (we have to add a new module for this: `Fake.DotNet.Testing.NUnit`):

```fsharp
#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.DotNet.Testing.NUnit
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.DotNet
open Fake.DotNet.Testing
open Fake.Core

// Properties
let buildDir = "./build/"
let testDir  = "./test/"

// Targets
Target.create "Clean" (fun _ ->
    Shell.CleanDirs [buildDir; testDir]
)

Target.create "BuildApp" (fun _ ->
   !! "src/app/**/*.csproj"
     |> MSBuild.runRelease id buildDir "Build"
     |> Trace.logItems "AppBuild-Output: "
)

Target.create "BuildTest" (fun _ ->
    !! "src/test/**/*.csproj"
      |> MSBuild.runDebug id testDir "Build"
      |> Trace.logItems "TestBuild-Output: "
)

Target.create "Test" (fun _ ->
    !! (testDir + "/NUnit.Test.*.dll")
      |> NUnit3.run (fun p ->
          {p with
                ShadowCopy = false })
)

Target.create "Default" (fun _ ->
    Trace.trace "Hello World from FAKE"
)

// Dependencies
open Fake.Core.TargetOperators
"Clean"
  ==> "BuildApp"
  ==> "BuildTest"
  ==> "Test"
  ==> "Default"

// start build
Target.runOrDefault "Default"
```

Our new *Test* target scans the test directory for test assemblies and runs them with the NUnit runner. FAKE automatically tries to locate the runner in one of your subfolders. See the [NUnit task documentation](apidocs/v5/fake-nunitsequential.html) if you need to specify the tool path explicitly.

The mysterious part **(fun p -> ...)** simply overrides the default parameters of the NUnit task and allows to specify concrete parameters.

![alt text](pics/gettingstarted/alltestsgreen.png "All tests green")

## What's next?

* Add more modules specific to your application and discover the Fake-APIs
* look at the [quick start guide](fake-dotnetcore.html) which has the same information in a more dense form.
* look at some of the samples in [FakeBuild](https://github.com/FakeBuild)
* look at [FAKEs own build script](https://github.com/fsharp/FAKE/blob/master/build.fsx) or other examples across the F# ecosystem.
* Add fake build scripts to your projects and let us know.
* Automate stuff with FAKE and use standalone scripts.
* Write your own modules and let us know - we love to add them to the nagivation or announce them on [twitter](https://twitter.com/fsharpMake).
* Contribute :)
