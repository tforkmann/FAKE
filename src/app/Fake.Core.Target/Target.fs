﻿namespace Fake.Core

open System
open System.Collections.Generic
open Fake.Core
open System.Threading.Tasks
open System.Threading
open FSharp.Control.Reactive

module internal TargetCli =
    let targetCli =
        """
Usage:
  fake-run --list
  fake-run --version
  fake-run --help | -h
  fake-run [target_opts] [target <target>] [--] [<targetargs>...]

Target Module Options [target_opts]:
    -t, --target <target>
                          Run the given target (ignored if positional argument 'target' is given)
    -e, --environment-variable <keyval> [*]
                          Set an environment variable. Use 'key=val'. Consider using regular arguments, see https://fake.build/core-targets.html
    -s, --single-target    Run only the specified target.
    -p, --parallel <num>  Run parallel with the given number of tasks.
        """
    let doc = Docopt(targetCli)
    let parseArgs args = doc.Parse args
/// [omit]
type TargetDescription = string

[<NoComparison>]
[<NoEquality>]
type TargetResult =
    { Error : exn option; Time : TimeSpan; Target : Target; WasSkipped : bool }

and [<NoComparison>] [<NoEquality>] TargetContext =
    { PreviousTargets : TargetResult list
      AllExecutingTargets : Target list
      FinalTarget : string
      Arguments : string list
      IsRunningFinalTargets : bool
      CancellationToken : CancellationToken }
    static member Create ft all args token = {
        FinalTarget = ft
        AllExecutingTargets = all
        PreviousTargets = []
        Arguments = args
        IsRunningFinalTargets = false
        CancellationToken = token }
    member x.HasError =
        x.PreviousTargets
        |> List.exists (fun t -> t.Error.IsSome)
    member x.TryFindPrevious name =
        x.PreviousTargets |> List.tryFind (fun t -> t.Target.Name = name)
    member x.TryFindTarget name =
        x.AllExecutingTargets |> List.tryFind (fun t -> t.Name = name)
    member x.ErrorTargets = 
        x.PreviousTargets |> List.choose (fun tres -> match tres.Error with
                                                      | Some er -> Some (er, tres.Target)
                                                      | None -> None)    

and [<NoComparison>] [<NoEquality>] TargetParameter =
    { TargetInfo : Target
      Context : TargetContext }

/// [omit]
and [<NoComparison>] [<NoEquality>] Target =
    { Name: string;
      Dependencies: string list;
      SoftDependencies: string list;
      Description: TargetDescription option;
      Function : TargetParameter -> unit}
    member x.DescriptionAsString = 
        match x.Description with 
        | Some d -> d 
        | _ -> null

/// Exception for request errors
#if !NETSTANDARD1_6
[<System.Serializable>]
#endif
type BuildFailedException =
    val private info : TargetContext option
    inherit Exception
    new (msg:string, inner:exn) = {
      inherit Exception(msg, inner)
      info = None }
    new (info:TargetContext, msg:string, inner:exn) = {
      inherit Exception(msg, inner)
      info = Some info }
#if !NETSTANDARD1_6
    new (info:System.Runtime.Serialization.SerializationInfo, context:System.Runtime.Serialization.StreamingContext) = {
      inherit Exception(info, context)
      info = None
    }
#endif
    member x.Info with get () = x.info
    member x.Wrap() =
        match x.info with
        | Some info ->
            BuildFailedException(info, x.Message, x:>exn)
        | None ->
            BuildFailedException(x.Message, x:>exn)

[<RequireQualifiedAccess>]
module Target =

    type private DependencyType =
        | Hard = 1
        | Soft = 2

    /// [omit]
    //let mutable PrintStackTraceOnError = false
    let private printStackTraceOnErrorVar = "Fake.Core.Target.PrintStackTraceOnError"
    let private getPrintStackTraceOnError, _, (setPrintStackTraceOnError:bool -> unit) =
        Fake.Core.FakeVar.define printStackTraceOnErrorVar

    /// [omit]
    //let mutable LastDescription = null
    let private lastDescriptionVar = "Fake.Core.Target.LastDescription"
    let private getLastDescription, removeLastDescription, setLastDescription =
        Fake.Core.FakeVar.define lastDescriptionVar

    /// Sets the Description for the next target.
    /// [omit]
    let description text =
        match getLastDescription() with
        | Some (v:string) ->
            failwithf "You can't set the description for a target twice. There is already a description: %A" v
        | None ->
           setLastDescription text

    /// Sets the Description for the next target.
    /// [omit]
    [<Obsolete("Use Target.description instead")>]
    let Description text = description text

    /// TargetDictionary
    /// [omit]
    let internal getVarWithInit name f =
        let varName = sprintf "Fake.Core.Target.%s" name
        let getVar, _, setVar =
            Fake.Core.FakeVar.define varName
        fun () ->
            match getVar() with
            | Some d -> d
            | None ->
                let d = f () // new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase)
                setVar d
                d

    let internal getTargetDict =
        getVarWithInit "TargetDict" (fun () -> new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase))

    /// Final Targets - stores final targets and if they are activated.
    let internal getFinalTargets =
        getVarWithInit "FinalTargets" (fun () -> new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase))

    /// BuildFailureTargets - stores build failure targets and if they are activated.
    let internal getBuildFailureTargets =
        getVarWithInit "BuildFailureTargets" (fun () -> new Dictionary<_,_>(StringComparer.OrdinalIgnoreCase))


    /// Resets the state so that a deployment can be invoked multiple times
    /// [omit]
    let internal reset() =
        getTargetDict().Clear()
        getBuildFailureTargets().Clear()
        getFinalTargets().Clear()

    /// Returns a list with all target names.
    let internal getAllTargetsNames() = getTargetDict() |> Seq.map (fun t -> t.Key) |> Seq.toList

    /// Gets a target with the given name from the target dictionary.
    let get name =
        let d = getTargetDict()
        match d.TryGetValue (name) with
        | true, target -> target
        | _  ->
            Trace.traceError <| sprintf "Target \"%s\" is not defined. Existing targets:" name
            for target in d do
                Trace.traceError  <| sprintf "  - %s" target.Value.Name
            failwithf "Target \"%s\" is not defined." name

    /// Returns the DependencyString for the given target.
    let internal dependencyString target =
        if target.Dependencies.IsEmpty then String.Empty else
        target.Dependencies
          |> Seq.map (fun d -> (get d).Name)
          |> String.separated ", "
          |> sprintf "(==> %s)"

    let internal runSimpleInternal context target =
        let watch = System.Diagnostics.Stopwatch.StartNew()
        let error =
            try
                if not context.IsRunningFinalTargets then
                    context.CancellationToken.ThrowIfCancellationRequested()|>ignore
                target.Function { TargetInfo = target; Context = context }
                None
            with e -> Some e
        watch.Stop()
        { Error = error; Time = watch.Elapsed; Target = target; WasSkipped = false }
    
    let internal runSimpleContextInternal (traceStart: string -> string -> string -> Trace.ISafeDisposable) context target =
        use t = traceStart target.Name target.DescriptionAsString (dependencyString target)
        let result = runSimpleInternal context target
        if result.Error.IsSome then 
            t.MarkFailed()
        else 
            t.MarkSuccess()
        { context with PreviousTargets = context.PreviousTargets @ [result] }

    /// This simply runs the function of a target without doing anything (like tracing, stopwatching or adding it to the results at the end)
    let runSimple name args =
        let target = get name
        target
        |> runSimpleInternal (TargetContext.Create name [target] args CancellationToken.None)

    /// This simply runs the function of a target without doing anything (like tracing, stopwatching or adding it to the results at the end)
    let runSimpleWithContext name ctx =
        let target = get name
        target
        |> runSimpleInternal ctx

    /// Returns the soft  DependencyString for the given target.
    let internal softDependencyString target =
        if target.SoftDependencies.IsEmpty then String.Empty else
        target.SoftDependencies
          |> Seq.map (fun d -> (get d).Name)
          |> String.separated ", "
          |> sprintf "(?=> %s)"

    /// Do nothing - Can be used to define empty targets.
    [<Obsolete("Use ignore instead")>]
    let DoNothing = (fun (_:TargetParameter) -> ())

    /// Checks whether the dependency (soft or normal) can be added.
    /// [omit]
    let internal checkIfDependencyCanBeAddedCore fGetDependencies targetName dependentTargetName =
        let target = get targetName
        let dependentTarget = get dependentTargetName
        let visited = HashSet<string>(StringComparer.OrdinalIgnoreCase)

        let rec checkDependencies dependentTarget =
            if visited.Add dependentTarget.Name then
                fGetDependencies dependentTarget
                |> List.iter (fun dep ->
                    if String.Equals(dep, targetName, StringComparison.OrdinalIgnoreCase) then
                        failwithf "Cyclic dependency between %s and %s" targetName dependentTarget.Name
                    checkDependencies (get dep))

        checkDependencies dependentTarget
        target,dependentTarget

    /// Checks whether the dependency can be added.
    /// [omit]
    let internal checkIfDependencyCanBeAdded targetName dependentTargetName =
       checkIfDependencyCanBeAddedCore (fun target -> target.Dependencies) targetName dependentTargetName

    /// Checks whether the soft dependency can be added.
    /// [omit]
    let internal checkIfSoftDependencyCanBeAdded targetName dependentTargetName =
       checkIfDependencyCanBeAddedCore (fun target -> target.SoftDependencies) targetName dependentTargetName

    /// Adds the dependency to the front of the list of dependencies.
    /// [omit]
    let internal dependencyAtFront targetName dependentTargetName =
        let target,_ = checkIfDependencyCanBeAdded targetName dependentTargetName

        let hasDependency =
           target.Dependencies
           |> Seq.exists (fun d -> String.Equals(d, dependentTargetName, StringComparison.OrdinalIgnoreCase))
        if not hasDependency then
            getTargetDict().[targetName] <- 
                { target with 
                    Dependencies = dependentTargetName :: target.Dependencies
                    SoftDependencies =
                        target.SoftDependencies
                        |> List.filter (fun d -> not (String.Equals(d, dependentTargetName, StringComparison.OrdinalIgnoreCase)))
                }

    /// Appends the dependency to the list of soft dependencies.
    /// [omit]
    let internal softDependencyAtFront targetName dependentTargetName =
        let target,_ = checkIfDependencyCanBeAdded targetName dependentTargetName

        let hasDependency =
           target.Dependencies
           |> Seq.exists (fun d -> String.Equals(d, dependentTargetName, StringComparison.OrdinalIgnoreCase))
        let hasSoftDependency =
           target.SoftDependencies
           |> Seq.exists (fun d -> String.Equals(d, dependentTargetName, StringComparison.OrdinalIgnoreCase))
        match hasDependency, hasSoftDependency with
        | true, _ -> ()
        | false, true -> ()
        | false, false ->
            getTargetDict().[targetName] <- { target with SoftDependencies = dependentTargetName :: target.SoftDependencies }

    /// Adds the dependency to the list of dependencies.
    /// [omit]
    let internal dependency targetName dependentTargetName = dependencyAtFront targetName dependentTargetName

    /// Adds the dependency to the list of soft dependencies.
    /// [omit]
    let internal softDependency targetName dependentTargetName = softDependencyAtFront targetName dependentTargetName

    /// Adds the dependencies to the list of dependencies.
    /// [omit]
    let internal Dependencies targetName dependentTargetNames = dependentTargetNames |> List.iter (dependency targetName)

    /// Adds the dependencies to the list of soft dependencies.
    /// [omit]
    let internal SoftDependencies targetName dependentTargetNames = dependentTargetNames |> List.iter (softDependency targetName)

    /// Backwards dependencies operator - x is dependent on ys.
    let inline internal (<==) x ys = Dependencies x ys

    /// Creates a target from template.
    /// [omit]
    let internal addTarget target name =
        getTargetDict().Add(name, target)
        name <== target.Dependencies
        removeLastDescription()

    /// add a target with dependencies
    /// [omit]
    let internal addTargetWithDependencies dependencies body name =
        let template =
            { Name = name
              Dependencies = dependencies
              SoftDependencies = []
              Description = getLastDescription()
              Function = body }
        addTarget template name

    /// Creates a Target.
    let create name body = addTargetWithDependencies [] body name

    /// Runs all activated final targets (in alphabetically order).
    /// [omit]
    let internal runFinalTargets context =
        getFinalTargets()
        |> Seq.filter (fun kv -> kv.Value)     // only if activated
        |> Seq.map (fun kv -> get kv.Key)
        |> Seq.fold (fun context target -> runSimpleContextInternal Trace.traceFinalTarget context target) context                

    /// Runs all build failure targets.
    /// [omit]
    let internal runBuildFailureTargets (context) =
        getBuildFailureTargets()
        |> Seq.filter (fun kv -> kv.Value)     // only if activated
        |> Seq.map (fun kv -> get kv.Key)
        |> Seq.fold (fun context target -> runSimpleContextInternal Trace.traceFailureTarget context target) context     

    /// List all targets available.
    let listAvailable() =
        Trace.log "The following targets are available:"
        for t in getTargetDict().Values |> Seq.sortBy (fun t -> t.Name) do
            Trace.logfn "   %s%s" t.Name (match t.Description with Some s -> sprintf " - %s" s | _ -> "")


    // Maps the specified dependency type into the list of targets
    let private withDependencyType (depType:DependencyType) targets =
        targets |> List.map (fun t -> depType, t)

    // Helper function for visiting targets in a dependency tree. Returns a set containing the names of the all the
    // visited targets, and a list containing the targets visited ordered such that dependencies of a target appear earlier
    // in the list than the target.
    let private visitDependencies repeatVisit fVisit targetName =
        let visit fGetDependencies fVisit targetName =
            let visited = new HashSet<_>(StringComparer.OrdinalIgnoreCase)
            let rec visitDependenciesAux orderedTargets = function
                // NOTE: should be tail recursive
                | (level, depType, targetName) :: workLeft ->
                    let target = get targetName
                    match visited.Add targetName with
                    | added when added || repeatVisit ->
                        fVisit (target, depType, level)
                        let newLeft = (fGetDependencies target |> Seq.map (fun (depType, targetName) -> (level + 1, depType, targetName)) |> Seq.toList) @ workLeft
                        let newOrdered = if added then (targetName :: orderedTargets) else orderedTargets
                        visitDependenciesAux newOrdered newLeft
                    | _ ->
                        visitDependenciesAux orderedTargets workLeft                        
                | _ -> orderedTargets
            let orderedTargets = visitDependenciesAux [] [(0, DependencyType.Hard, targetName)]
            visited, orderedTargets

        // First pass is to accumulate targets in (hard) dependency graph
        let visited, _ = visit (fun t -> t.Dependencies |> List.rev |> withDependencyType DependencyType.Hard) ignore targetName

        let getAllDependencies (t: Target) =
             (t.Dependencies |> List.rev |> withDependencyType DependencyType.Hard) @
             // Note that we only include the soft dependency if it is present in the set of targets that were
             // visited.
             (t.SoftDependencies |> List.filter visited.Contains |> withDependencyType DependencyType.Soft)

        // Now make second pass, adding in soft depencencies if appropriate
        visit getAllDependencies fVisit targetName

    /// <summary>Writes a dependency graph.</summary>
    /// <param name="verbose">Whether to print verbose output or not.</param>
    /// <param name="target">The target for which the dependencies should be printed.</param>
    let printDependencyGraph verbose target =
        match getTargetDict().TryGetValue (target) with
        | false,_ -> listAvailable()
        | true,target ->
            let sb = System.Text.StringBuilder()
            let appendfn fmt = Printf.ksprintf (sb.AppendLine >> ignore) fmt

            appendfn "%sDependencyGraph for Target %s:" (if verbose then String.Empty else "Shortened ") target.Name
            let logDependency ((t: Target), depType, level) =
                let indent = (String(' ', level * 3))
                if depType = DependencyType.Soft then
                    appendfn "%s<=? %s" indent t.Name
                else
                    appendfn "%s<== %s" indent t.Name

            let _ = visitDependencies verbose logDependency target.Name
            //appendfn ""
            //sb.Length <- sb.Length - Environment.NewLine.Length
            Trace.log <| sb.ToString()

    let internal printRunningOrder (targetOrder:Target[] list) =
        let sb = System.Text.StringBuilder()
        let appendfn fmt = Printf.ksprintf (sb.AppendLine >> ignore) fmt
        appendfn "The running order is:"
        targetOrder
        |> List.iteri (fun index x ->
                                //if (environVarOrDefault "parallel-jobs" "1" |> int > 1) then
                                appendfn "Group - %d" (index + 1)
                                Seq.iter (appendfn "  - %s") (x|>Seq.map (fun t -> t.Name)))

        sb.Length <- sb.Length - Environment.NewLine.Length
        Trace.log <| sb.ToString()

    /// <summary>Writes a build time report.</summary>
    /// <param name="total">The total runtime.</param>
    let internal writeTaskTimeSummary total context =
        Trace.traceHeader "Build Time Report"
        let executedTargets = context.PreviousTargets
        if executedTargets.Length > 0 then
            let width =
                executedTargets
                  |> Seq.map (fun (tres) -> tres.Target.Name.Length)
                  |> Seq.max
                  |> max 8

            let alignedString (name:string) (duration) extra =
                let durString = sprintf "%O" duration
                if (String.IsNullOrEmpty extra) then
                    sprintf "%s   %s" (name.PadRight width) durString
                else sprintf "%s   %s   (%s)" (name.PadRight width) (durString.PadRight "00:00:00.0000824".Length) extra
            let aligned (name:string) duration extra = alignedString name duration extra |> Trace.trace
            let alignedWarn (name:string) duration extra = alignedString name duration extra |> Trace.traceFAKE "%s"
            let alignedError (name:string) duration extra = alignedString name duration extra |> Trace.traceError

            aligned "Target" "Duration" null
            aligned "------" "--------" null
            executedTargets
              |> Seq.iter (fun (tres) ->
                    let name = tres.Target.Name
                    let time = tres.Time
                    match tres.Error with
                    | None when tres.WasSkipped -> alignedWarn name time "skipped" // Yellow
                    | None -> aligned name time null
                    | Some e -> alignedError name time e.Message)

            aligned "Total:" total null
            if not context.HasError then 
                aligned "Status:" "Ok" null
            else
                alignedError "Status:" "Failure" null
        else
            Trace.traceError "No target was successfully completed"

        Trace.traceLine()

    /// Determines a parallel build order for the given set of targets
    let internal determineBuildOrder (target : string) =
        let _ = get target

        let rec visitDependenciesAux previousDependencies = function
            // NOTE: should be tail recursive
            | (visited, level, targetName) :: workLeft ->
                let target = get targetName
                let isVisited =
                    visited
                    |> Seq.exists (fun t -> String.Equals(t, targetName, StringComparison.OrdinalIgnoreCase))
                if isVisited then
                    visitDependenciesAux previousDependencies workLeft
                else
                    let deps =
                        target.Dependencies
                        |> List.map (fun (t) -> (String.toLower targetName::visited), level + 1, t)
                    let newVisitedDeps = target :: previousDependencies
                    visitDependenciesAux newVisitedDeps (deps @ workLeft)
            | _ ->
                previousDependencies
                |> List.distinctBy (fun (t) -> String.toLower t.Name)

        // first find the list of targets we "have" to build
        let targets = visitDependenciesAux [] [[], 0, target]

        // Try to build the optimal tree by starting with the targets without dependencies and remove them from the list iteratively
        let targetLeftSet = HashSet<_>(StringComparer.OrdinalIgnoreCase)
        targets |> Seq.map (fun t -> t.Name) |> Seq.iter (targetLeftSet.Add >> ignore)
        let rec findOrder progress (targetLeft:Target list) =
            // NOTE: Should be tail recursive
            let isValidTarget name =
                targetLeftSet.Contains(name)
            
            let canBeExecuted (t:Target) =
                t.Dependencies @ t.SoftDependencies
                |> Seq.filter isValidTarget
                |> Seq.isEmpty
            let map =
                targetLeft
                    |> Seq.groupBy canBeExecuted
                    |> Seq.map (fun (t, g) -> t, Seq.toList g)
                    |> dict
            let execute, left =
                (match map.TryGetValue true with
                | true, ts -> ts
                | _ -> []),
                match map.TryGetValue false with
                | true, ts -> ts
                | _ -> []
            if List.isEmpty execute then failwithf "Could not progress build order in %A" targetLeft
            if List.isEmpty left then
                List.rev (List.toArray execute :: progress)
            else
                execute |> Seq.map (fun t -> t.Name) |> Seq.iter (targetLeftSet.Remove >> ignore)
                findOrder (List.toArray execute :: progress) left      
        findOrder [] targets

    /// Runs a single target without its dependencies... only when no error has been detected yet.
    let internal runSingleTarget (target : Target) (context:TargetContext) =
        if not context.HasError then
            runSimpleContextInternal Trace.traceTarget context target
        else
            { context with PreviousTargets = context.PreviousTargets @ [{ Error = None; Time = TimeSpan.Zero; Target = target; WasSkipped = true }] }

    module internal ParallelRunner =
        let internal mergeContext (ctx1:TargetContext) (ctx2:TargetContext) =
            let known =
                ctx1.PreviousTargets
                |> Seq.map (fun tres -> String.toLower tres.Target.Name, tres)
                |> dict
            let filterKnown targets =
                targets
                |> List.filter (fun tres -> not (known.ContainsKey (String.toLower tres.Target.Name)))
            { ctx1 with
                PreviousTargets =
                    ctx1.PreviousTargets @ filterKnown ctx2.PreviousTargets
            }

        // Centralized handling of target context and next target logic...
        [<NoComparison>]
        [<NoEquality>]
        type RunnerHelper =
            | GetNextTarget of TargetContext * AsyncReplyChannel<Async<TargetContext * Target option>>
        type IRunnerHelper =
            abstract GetNextTarget : TargetContext -> Async<TargetContext * Target option>
        let createCtxMgr (order:Target[] list) (ctx:TargetContext) =
            let body (inbox:MailboxProcessor<RunnerHelper>) = async {
                let targetCount =
                    order |> Seq.sumBy (fun t -> t.Length)
                let resolution = Set.ofSeq(order |> Seq.concat |> Seq.map (fun t -> String.toLower t.Name))
                let inResolution (t:string) = resolution.Contains (String.toLower t)
                let mutable ctx = ctx
                let mutable waitList = []
                let mutable runningTasks = []
                //let mutable remainingOrders = order
                try
                    while true do
                        let! msg = inbox.Receive()
                        match msg with
                        | GetNextTarget (newCtx, reply) ->
                            let failwithf pf =
                                // handle reply before throwing.
                                let tcs = new TaskCompletionSource<TargetContext * Target option>()
                                waitList <- waitList @ [ tcs ]
                                reply.Reply (tcs.Task |> Async.AwaitTask)
                                failwithf pf
                            // semantic is:
                            // - We never return a target twice!
                            // - we fill up the waitlist first
                            ctx <- mergeContext ctx newCtx
                            let known =
                                ctx.PreviousTargets
                                |> Seq.map (fun tres -> String.toLower tres.Target.Name, tres)
                                |> dict
                            runningTasks <-
                                runningTasks
                                |> List.filter (fun t -> not(known.ContainsKey (String.toLower t.Name)))
                            if known.Count = targetCount then
                                for (w:System.Threading.Tasks.TaskCompletionSource<TargetContext * Target option>) in waitList do
                                    w.SetResult (ctx, None)
                                waitList <- []
                                reply.Reply (async.Return(ctx, None))
                            else
                                let isRunnable (t:Target) =
                                    not (known.ContainsKey (String.toLower t.Name)) && // not already finised
                                    not (runningTasks |> Seq.exists (fun r -> String.toLower r.Name = String.toLower t.Name)) && // not already running
                                    t.Dependencies @ List.filter inResolution t.SoftDependencies // all dependencies finished
                                    |> Seq.forall (String.toLower >> known.ContainsKey)
                                let runnable =
                                    order
                                    |> Seq.concat
                                    |> Seq.filter isRunnable
                                    |> Seq.toList

                                let rec getNextFreeRunableTarget (r) =
                                    match r with
                                    | t :: rest ->
                                        match waitList with
                                        | h :: restwait ->
                                            // fill some idle worker
                                            runningTasks <- t :: runningTasks
                                            h.SetResult (ctx, Some t)
                                            waitList <- restwait
                                            getNextFreeRunableTarget rest
                                        | [] -> Some t
                                    | [] -> None
                                match getNextFreeRunableTarget runnable with
                                | Some free ->
                                    runningTasks <- free :: runningTasks
                                    reply.Reply (async.Return(ctx, Some free))
                                | None ->
                                    if runningTasks.Length = 0 && resolution.Count > known.Count then
                                        // No running tasks but still open resolution
                                        let resolutionStr = sprintf "[%s]" (String.Join(",", resolution))
                                        let knownStr = sprintf "[%s]" (String.Join(",", known.Keys))
                                        failwithf "Error detected in fake scheduler: resolution '%s', known '%s'" resolutionStr knownStr
                                    // queue work
                                    let tcs = new TaskCompletionSource<TargetContext * Target option>()
                                    waitList <- waitList @ [ tcs ]
                                    reply.Reply (tcs.Task |> Async.AwaitTask)
                with e ->
                    for (w:System.Threading.Tasks.TaskCompletionSource<TargetContext * Target option>) in waitList do
                        w.SetException (exn("mailbox failed", e))
                    waitList <- []
                    while true do
                        let! msg = inbox.Receive()
                        match msg with
                        | GetNextTarget (_, reply) ->
                            reply.Reply (async { return raise <| exn("mailbox failed", e) })
            }

            let mbox = MailboxProcessor.Start(body)
            { new IRunnerHelper with
                member __.GetNextTarget (ctx) = async {
                    let! repl = mbox.PostAndAsyncReply(fun reply -> GetNextTarget(ctx, reply))
                    return! repl
                }
            }

        let runOptimal workerNum (order:Target[] list) targetContext =
            let mgr = createCtxMgr order targetContext
            let targetRunner () =
                async {
                    let token = targetContext.CancellationToken
                    let! (tctx, tt) = mgr.GetNextTarget(targetContext)
                    let mutable ctx = tctx
                    let mutable nextTarget = tt
                    while nextTarget.IsSome && not token.IsCancellationRequested do
                        let newCtx = runSingleTarget nextTarget.Value ctx
                        let! (tctx, tt) = mgr.GetNextTarget(newCtx)
                        ctx <- tctx
                        nextTarget <- tt
                    return ctx
                } |> Async.StartAsTask
            Array.init workerNum (fun _ -> targetRunner())
            |> Task.WhenAll
            |> Async.AwaitTask
            |> Async.RunSynchronously
            |> Seq.reduce mergeContext

    let private handleUserCancelEvent (cts:CancellationTokenSource) (e:ConsoleCancelEventArgs)=
        e.Cancel <- true
        printfn "Gracefully shutting down.."
        printfn "Press ctrl+c again to force quit"
        let __ =
            Console.CancelKeyPress
            |> Observable.first
            |> Observable.subscribe (fun _ ->  Environment.Exit 1)
        Process.killAllCreatedProcesses() |> ignore
        cts.Cancel()
    
    /// Optional `TargetContext`
    type OptionalTargetContext = 
        private
            | Set of TargetContext
            | MaybeSet of TargetContext option
        member x.Context =
            match x with
            | Set t -> Some t
            | MaybeSet o -> o

    /// Runs a target and its dependencies.
    let internal runInternal singleTarget parallelJobs targetName args =
        match getLastDescription() with
        | Some d -> failwithf "You set a task description (%A) but didn't specify a task. Make sure to set the Description above the Target." d
        | None -> ()

        printfn "run %s" targetName
        let watch = new System.Diagnostics.Stopwatch()
        watch.Start()

        Trace.tracefn "Building project with version: %s" BuildServer.buildVersion
        printDependencyGraph false targetName

        // determine a build order
        let order = determineBuildOrder targetName
        printRunningOrder order
        if singleTarget
        then Trace.traceImportant "Single target mode ==> Skipping dependencies."
        let allTargets = List.collect Seq.toList order
        use cts = new CancellationTokenSource()
        let context = TargetContext.Create targetName allTargets args cts.Token

        let context =
            let captureContext (f:'a->unit) =
                let ctx = Context.getExecutionContext()
                (fun a ->
                    let nctx = Context.getExecutionContext()
                    if ctx <> nctx then Context.setExecutionContext ctx
                    f a)

            let cancelHandler  = captureContext (handleUserCancelEvent cts)
            use __ =
                Console.CancelKeyPress
                |> Observable.first
                |> Observable.subscribe cancelHandler

            let context =
                // Figure out the order in in which targets can be run, and which can be run in parallel.
                if parallelJobs > 1 && not singleTarget then
                    Trace.tracefn "Running parallel build with %d workers" parallelJobs
                    // always try to keep "parallelJobs" runners busy
                    ParallelRunner.runOptimal parallelJobs order context
                else
                    let targets = order |> Seq.collect id |> Seq.toArray
                    let lastTarget = targets |> Array.last
                    if singleTarget then
                        runSingleTarget lastTarget context
                    else
                        targets |> Array.fold (fun context target -> runSingleTarget target context) context

            if context.HasError && not context.CancellationToken.IsCancellationRequested then
                    runBuildFailureTargets context
            else 
                context

        let context = runFinalTargets {context with IsRunningFinalTargets=true}
        writeTaskTimeSummary watch.Elapsed context
        context           

    /// Creates a target in case of build failure (not activated).
    let createBuildFailure name body =
        create name body
        getBuildFailureTargets().Add(name,false)

    /// Activates the build failure target.
    let activateBuildFailure name =
        let _ = get name // test if target is defined
        getBuildFailureTargets().[name] <- true

    /// Deactivates the build failure target.
    let deactivateBuildFailure name =
        let t = get name // test if target is defined
        getBuildFailureTargets().[name] <- false

    /// Creates a final target (not activated).
    let createFinal name body =
        create name body
        getFinalTargets().Add(name,false)

    /// Activates the final target.
    let activateFinal name =
        let _ = get name // test if target is defined
        getFinalTargets().[name] <- true

    /// deactivates the final target.
    let deactivateFinal name =
        let t = get name // test if target is defined
        getFinalTargets().[name] <- false

    let internal getBuildFailedException (context:TargetContext) =
        let targets = context.ErrorTargets |> Seq.map (fun (_er, target) -> target.Name) |> Seq.distinct
        let targetStr = String.Join(", ", targets)
        let errorMsg =
            if context.ErrorTargets.Length = 1 then
                sprintf "Target '%s' failed." targetStr
            else
                sprintf "Targets '%s' failed." targetStr
        let inner = AggregateException(AggregateException().Message, context.ErrorTargets |> Seq.map fst)
        BuildFailedException(context, errorMsg, inner)

    /// Updates build status based on `OptionalTargetContext`
    /// Will not update status if `OptionalTargetContext` is `MaybeSet` with value `None`
    let updateBuildStatus (context:OptionalTargetContext) =
        match context.Context with
        | Some c when c.PreviousTargets.Length = 0 -> Trace.setBuildState TagStatus.Warning
        | Some c when c.HasError -> let targets = c.ErrorTargets |> Seq.map (fun (_er, target) -> target.Name) |> Seq.distinct
                                    let targetStr = String.Join(", ", targets)
                                    if c.ErrorTargets.Length = 1 then
                                        Trace.setBuildStateWithMessage TagStatus.Failed (sprintf "Target '%s' failed." targetStr)
                                    else
                                        Trace.setBuildStateWithMessage TagStatus.Failed (sprintf "Targets '%s' failed." targetStr)                                    
        | Some _ -> Trace.setBuildState TagStatus.Success
        | _ -> ()

    /// If `TargetContext option` is Some and has error, raise it as a BuildFailedException
    let raiseIfError (context:OptionalTargetContext) =
        let c = context.Context
        if c.IsSome && c.Value.HasError && not c.Value.CancellationToken.IsCancellationRequested then
            getBuildFailedException c.Value
            |> raise


    /// Runs a target and its dependencies and returns a `TargetContext`
    [<Obsolete "Use Target.WithContext.run instead">]
    let runAndGetContext parallelJobs targetName args = runInternal false parallelJobs targetName args
    let internal getRunFunction allowArgs defaultTarget =
        let ctx = Fake.Core.Context.forceFakeContext ()
        let trySplitEnvArg (arg:string) =
            let idx = arg.IndexOf('=')
            if idx < 0 then
                Trace.traceError (sprintf "Argument for -e should contain '=' but was '%s', the argument will be ignored." arg)
                None
            else
                Some (arg.Substring(0, idx), arg.Substring(idx + 1))
        let results =
            try
                let res = TargetCli.parseArgs (ctx.Arguments |> List.toArray)
                res |> Choice1Of2
            with :? DocoptException as e -> Choice2Of2 e
        match results with
        | Choice1Of2 results ->
            let envs =
                match DocoptResult.tryGetArguments "--environment-variable" results with
                | Some args ->
                    args |> List.choose trySplitEnvArg
                | None -> []
            for (key, value) in envs do Environment.setEnvironVar key value

            if DocoptResult.hasFlag "--list" results then
                listAvailable()
                None
            elif DocoptResult.hasFlag "-h" results || DocoptResult.hasFlag "--help" results then
                printfn "%s" TargetCli.targetCli
                printfn "Hint: Run 'fake run <build.fsx> target <target> --help' to get help from your target."
                None
            elif DocoptResult.hasFlag "--version" results then
                printfn "Target Module Version: %s" AssemblyVersionInformation.AssemblyInformationalVersion
                None
            else
                let target =
                    match DocoptResult.tryGetArgument "<target>" results with
                    | None ->
                        match DocoptResult.tryGetArgument "--target" results with
                        | None ->
                            match Environment.environVarOrNone "target" with
                            | Some arg ->
                                Trace.log
                                    <| sprintf "Using target '%s' from the 'target' environment variable." arg
                                Some arg
                            | None -> None                                                                
                        | Some arg -> Some arg
                    | Some arg ->
                        match DocoptResult.tryGetArgument "--target" results with
                        | None -> ()
                        | Some innerArg ->
                            Trace.traceImportant
                                <| sprintf "--target '%s' is ignored when 'target %s' is given" innerArg arg
                        Some arg
                let parallelJobs =
                    match DocoptResult.tryGetArgument "--parallel" results with
                    | Some arg ->
                        match System.Int32.TryParse(arg) with
                        | true, i -> i
                        | _ -> failwithf "--parallel needs an integer argument, could not parse '%s'" arg
                    | None ->
                        Environment.environVarOrDefault "parallel-jobs" "1" |> int
                let singleTarget =
                    match DocoptResult.hasFlag "--single-target" results with
                    | true -> true
                    | false -> Environment.hasEnvironVar "single-target"
                let arguments =
                    match DocoptResult.tryGetArguments "<targetargs>" results with
                    | Some args -> args
                    | None -> []
                if not allowArgs && arguments <> [] then
                    failwithf "The following arguments could not be parsed: %A\nTo forward arguments to your targets you need to use \nTarget.runOrDefaultWithArguments instead of Target.runOrDefault" arguments
                match target, defaultTarget with
                | Some t, _ -> Some(fun () -> Some(runInternal singleTarget parallelJobs t arguments))
                | None, Some t -> Some(fun () -> Some(runInternal singleTarget parallelJobs t arguments))
                | None, None -> Some (fun () -> listAvailable()
                                                None)
        | Choice2Of2 e ->
            // To ensure exit code.
            raise <| exn (sprintf "Usage error: %s\n%s" e.Message TargetCli.targetCli, e)

    let private runFunction (targetFunction:(unit -> TargetContext option) Option) = 
        match targetFunction with
        | Some f -> OptionalTargetContext.MaybeSet(f())
        | _ -> OptionalTargetContext.MaybeSet(None)
    

    /// Run functions which don't throw and return the context after all targets have been executed.
    module WithContext =
        /// Runs a target and its dependencies and returns an `OptionalTargetContext`
        let run parallelJobs targetName args = runInternal false parallelJobs targetName args |> OptionalTargetContext.Set

        /// Runs the command given on the command line or the given target when no target is given & get context
        let runOrDefault defaultTarget =
            getRunFunction false (Some(defaultTarget)) |> runFunction

        /// Runs the command given on the command line or the given target when no target is given & get context
        let runOrDefaultWithArguments defaultTarget =
            getRunFunction true (Some(defaultTarget)) |> runFunction

        /// Runs the target given by the target parameter or lists the available targets & get context
        let runOrList() =
            getRunFunction false None |> runFunction
    
    /// Runs a target and its dependencies
    let run parallelJobs targetName args : unit =
        WithContext.run parallelJobs targetName args |> raiseIfError

    /// Runs the command given on the command line or the given target when no target is given
    let runOrDefault (defaultTarget:string) : unit =
        WithContext.runOrDefault defaultTarget |> raiseIfError

    /// Runs the command given on the command line or the given target when no target is given
    let runOrDefaultWithArguments (defaultTarget:string) : unit =
        WithContext.runOrDefaultWithArguments defaultTarget |> raiseIfError

    /// Runs the target given by the target parameter or lists the available targets
    let runOrList() : unit =
        WithContext.runOrList() |> raiseIfError
