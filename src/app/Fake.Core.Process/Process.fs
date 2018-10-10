﻿/// Contains functions which can be used to start other tools.

namespace Fake.Core

open System
open System.Diagnostics
open System

/// A record type which captures console messages
type ConsoleMessage = 
    { IsError : bool
      Message : string
      Timestamp : DateTimeOffset }
    static member Create isError msg =
        { IsError = isError; Message = msg; Timestamp = DateTimeOffset.UtcNow }
    static member CreateError msg = ConsoleMessage.Create true msg
    static member CreateOut msg = ConsoleMessage.Create false msg

/// A process result including error code, message log and errors.
type ProcessResult = 
    { ExitCode : int
      Results : ConsoleMessage list}
    member x.OK = x.ExitCode = 0
    member x.Messages =
        x.Results
        |> List.choose (function
            | { IsError = false } as m -> Some m.Message
            | _ -> None)
    member x.Errors =
        x.Results
        |> List.choose (function
            | { IsError = true } as m -> Some m.Message
            | _ -> None)
    static member New exitCode results = 
        { ExitCode = exitCode
          Results = results }

module private ProcStartInfoData =
    let defaultEnvVar = "__FAKE_CHECK_USER_ERROR"

    let createEnvironmentMap () =
        Environment.environVars () |> Map.ofSeq |> Map.add defaultEnvVar defaultEnvVar
    let checkMap (map:Map<string,string>) =
        if Environment.isWindows then
            let hs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            for kv in map do
                if not (hs.Add kv.Key) then
                    // Environment variables are case sensitive and this is invalid!
                    let existing = hs |> Seq.find (fun s -> s.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                    failwithf "Detected invalid environment map the key '%s' was used as '%s' as well, however in windows environment variables are case-insensitive. This error shouldn't happen if you use the process helpers like 'Process.setEnvironmentVariable' instead of setting the map manually." kv.Key existing

open ProcStartInfoData

type ProcStartInfo =
    { /// Gets or sets the set of command-line arguments to use when starting the application.
      Arguments : string
      /// Gets or sets a value indicating whether to start the process in a new window.
      CreateNoWindow : bool
      /// Gets or sets a value that identifies the domain to use when starting the process. If this value is null, the UserName property must be specified in UPN format.
      Domain : string
      /// Gets the environment variables that apply to this process and its child processes.
      /// NOTE: Recommendation is to not use this Field, but instead use the helper function in the Proc module (for example Process.setEnvironmentVariable)
      /// NOTE: This field is ignored when UseShellExecute is true.
      Environment : Map<string, string>
#if FX_ERROR_DIALOG
      /// Gets or sets a value indicating whether an error dialog box is displayed to the user if the process cannot be started.
      ErrorDialog : bool
      /// Gets or sets the window handle to use when an error dialog box is shown for a process that cannot be started.
      ErrorDialogParentHandle  : IntPtr
#endif  
      /// Gets or sets the application or document to start.
      FileName : string
      /// true if the Windows user profile should be loaded; otherwise, false. The default is false.
      LoadUserProfile : bool
      // Note: No SecureString as that one is obsolete anyway and to provide a uniform API across netstandard16.
      /// Gets or sets the user password in clear text to use when starting the process.
      Password : string
#if FX_WINDOWSTLE
      /// One of the enumeration values that indicates whether the process is started in a window that is maximized, minimized, normal (neither maximized nor minimized), or not visible. The default is Normal.
      WindowStyle : ProcessWindowStyle
#endif  
      /// true if error output should be written to Process.StandardError; otherwise, false. The default is false.
      RedirectStandardError : bool
      /// true if input should be read from Process.StandardInput; otherwise, false. The default is false.
      RedirectStandardInput : bool
      /// true if output should be written to Process.StandardOutput; otherwise, false. The default is false.
      RedirectStandardOutput : bool
      /// An object that represents the preferred encoding for error output. The default is null.
      StandardErrorEncoding : System.Text.Encoding
      /// An object that represents the preferred encoding for standard output. The default is null.
      StandardOutputEncoding : System.Text.Encoding
      /// The user name to use when starting the process. If you use the UPN format, user@DNS_domain_name, the Domain property must be null.
      UserName : string
      /// true if the shell should be used when starting the process; false if the process should be created directly from the executable file. The default is true.
      UseShellExecute : bool
#if FX_VERB
      /// The action to take with the file that the process opens. The default is an empty string (""), which signifies no action.
      Verb : string
#endif
      /// When UseShellExecute is true, the fully qualified name of the directory that contains the process to be started. When the UseShellExecute property is false, the working directory for the process to be started. The default is an empty string ("").
      WorkingDirectory : string
      } 
    static member Create() =
      { Arguments = null
        CreateNoWindow = false
        Domain = null
        Environment = createEnvironmentMap()
#if FX_ERROR_DIALOG
        ErrorDialog = false
        ErrorDialogParentHandle = IntPtr.Zero
#endif    
        FileName = ""
        LoadUserProfile = false
        Password = null
#if FX_WINDOWSTLE
        WindowStyle = ProcessWindowStyle.Normal
#endif
        RedirectStandardError = false
        RedirectStandardInput = false
        RedirectStandardOutput = false
        StandardErrorEncoding = null
        StandardOutputEncoding = null
        UserName = null
        UseShellExecute = true
#if FX_VERB
        Verb = ""
#endif
        WorkingDirectory = "" }
    [<Obsolete("Please use 'Create()' instead and make sure to properly set Environment via Process-module funtions!")>]    
    static member Empty = ProcStartInfo.Create()
    /// Sets the current environment variables.
    member x.WithEnvironment map =
        { x with Environment = map }

    member x.AsStartInfo =
        let p = new ProcessStartInfo(x.FileName, x.Arguments)
        p.CreateNoWindow <- x.CreateNoWindow
        if not (isNull x.Domain) then
            p.Domain <- x.Domain

        ProcStartInfoData.checkMap x.Environment
        match x.Environment |> Map.tryFind defaultEnvVar with
        | None -> failwithf "Your environment variables look like they are set manually, but you are missing the default variables. Use the `Process.` helpers to change the 'Environment' field to inherit default values! See https://github.com/fsharp/FAKE/issues/1776#issuecomment-365431982"
        | Some _ ->
            if not x.UseShellExecute then  
                p.Environment.Clear()
                x.Environment
                |> Map.remove defaultEnvVar
                |> Map.iter (fun var key ->
                    p.Environment.[var] <- key)

#if FX_ERROR_DIALOG
        if p.ErrorDialog then
            p.ErrorDialog <- x.ErrorDialog
            p.ErrorDialogParentHandle <- x.ErrorDialogParentHandle
#endif
        if x.LoadUserProfile then
            p.LoadUserProfile <- x.LoadUserProfile

        if not (isNull x.Password) then
#if FX_PASSWORD_CLEAR_TEXT
            p.PasswordInClearText <- x.Password
#else
#if FX_PASSWORD
            p.Password <-
               (let sec = new System.Security.SecureString()
                x.Password |> Seq.iter (sec.AppendChar)
                sec.MakeReadOnly()
                sec)
#else
            failwithf "Password for starting a process was set but with this compiled binary neither ProcessStartInfo.Password nor ProcessStartInfo.PasswordInClearText was available."
#endif
#endif
#if FX_WINDOWSTLE
        if ProcessWindowStyle.Normal <> x.WindowStyle then
            p.WindowStyle <- x.WindowStyle
#endif
        p.RedirectStandardError <- x.RedirectStandardError
        p.RedirectStandardInput <- x.RedirectStandardInput
        p.RedirectStandardOutput <- x.RedirectStandardOutput
        if not (isNull x.StandardErrorEncoding) then
            p.StandardErrorEncoding <- x.StandardErrorEncoding
        if not (isNull x.StandardOutputEncoding) then
            p.StandardOutputEncoding <- x.StandardOutputEncoding
        if not (isNull x.UserName) then
            p.UserName <- x.UserName
        p.UseShellExecute <- x.UseShellExecute
#if FX_VERB
        if "" <> x.Verb then
            p.Verb <- x.Verb
#endif    
        p.WorkingDirectory <- x.WorkingDirectory
        p

    /// Parameter type for process execution.
    type ExecParams = 
        { /// The path to the executable, without arguments. 
          Program : string
          /// The working directory for the program. Defaults to "".
          WorkingDir : string
          /// Command-line parameters in a string.
          CommandLine : string
          /// Command-line argument pairs. The value will be quoted if it contains
          /// a string, and the result will be appended to the CommandLine property.
          /// If the key ends in a letter or number, a space will be inserted between
          /// the key and the value.
          Args : (string * string) list }
        /// Default parameters for process execution.
        static member Empty =
            { Program = ""
              WorkingDir = ""
              CommandLine = ""
              Args = [] }


namespace Fake.Core

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Text
open System.Collections.Generic
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.Core.GuardedAwaitObservable


#if !FX_NO_HANDLE
module internal Kernel32 =
    open System
    open System.Text
    open System.Diagnostics
    open System.Runtime.InteropServices
    [<DllImport("Kernel32.dll", SetLastError = true)>]
    extern UInt32 QueryFullProcessImageName(IntPtr hProcess, UInt32 flags, StringBuilder text, [<Out>] UInt32& size)
    
    let getPathToApp (proc:Process) =
        let mutable nChars = 256u
        let Buff = new StringBuilder(int nChars);

        let success = QueryFullProcessImageName(proc.Handle, 0u, Buff, &nChars)

        if (0u <> success) then
            Buff.ToString()
        else
            let hresult = Marshal.GetHRForLastWin32Error()
            Marshal.ThrowExceptionForHR hresult
            "Error = " + string hresult + " when calling GetProcessImageFileName"
#endif

[<RequireQualifiedAccess>]
module Process =

    /// Kills the given process
    let kill (proc : Process) = 
        Trace.tracefn "Trying to kill process %s (Id = %d)" proc.ProcessName proc.Id
        try 
            proc.Kill()
        with ex -> Trace.logfn "Killing %s failed with %s" proc.ProcessName ex.Message

    type ProcessList() =
        let mutable shouldKillProcesses = true 
        let startedProcesses = HashSet()
        let killProcesses () = 
            let traced = ref false
            let processList = Process.GetProcesses()
            for pid, startTime in startedProcesses do
                try
                    match processList |> Seq.tryFind (fun p -> p.Id = pid) with
                    // process IDs may be reused by the operating system so we need
                    // to make sure the process is indeed the one we started
                    | Some proc when proc.StartTime = startTime && not proc.HasExited ->
                        try 
                            if not !traced then
                              Trace.tracefn "Killing all processes that are created by FAKE and are still running."
                              traced := true

                            Trace.logfn "Trying to kill %s" proc.ProcessName
                            kill proc
                        with exn -> Trace.logfn "Killing %s failed with %s" proc.ProcessName exn.Message
                    | _ -> ()                    
                with exn -> Trace.logfn "Killing %d failed with %s" pid exn.Message
            startedProcesses.Clear()
        member __.KillAll() = killProcesses()
        member __.Add (pid, startTime) = startedProcesses.Add(pid, startTime)
        member __.SetShouldKill (enable) = shouldKillProcesses <- enable
        member __.GetShouldKill = shouldKillProcesses

        interface IDisposable with
            member __.Dispose() =
                if shouldKillProcesses then killProcesses()

    /// [omit]
    //let startedProcesses = HashSet()
    let private startedProcessesVar = "Fake.Core.Process.startedProcesses"
    let private getStartedProcesses, _, private setStartedProcesses = 
        Fake.Core.FakeVar.defineAllowNoContext<ProcessList> startedProcessesVar

    let private doWithProcessList f =
        if Fake.Core.Context.isFakeContext () then
            match getStartedProcesses () with
            | Some h -> Some(f h)
            | None -> 
                let h = new ProcessList()
                setStartedProcesses (h)
                Some (f h)
        else None        

    let private addStartedProcess (id:int, startTime:System.DateTime) =
        doWithProcessList (fun h -> h.Add(id, startTime)) |> ignore

    let setKillCreatedProcesses (enable) =
        doWithProcessList (fun h -> h.SetShouldKill enable) |> ignore

    let shouldKillCreatedProcesses () =
        match doWithProcessList (fun h -> h.GetShouldKill) with
        | Some v -> v
        | None -> false

    //let private monoArgumentsVar = "Fake.Core.Process.monoArguments"
    //let private tryGetMonoArguments, _, public setMonoArguments = 
    //    Fake.Core.Context.fakeVar monoArgumentsVar
    //let getMonoArguments () =
    //    match tryGetMonoArguments () with
    //    | Some (args) -> args
    //    | None -> ""
    //
    ///// Modifies the ProcessStartInfo according to the platform semantics
    //let platformInfoAction (psi : ProcessStartInfo) = 
    //    if Environment.isMono && psi.FileName.EndsWith ".exe" then 
    //        psi.Arguments <- getMonoArguments() + " \"" + psi.FileName + "\" " + psi.Arguments
    //        psi.FileName <- Environment.monoPath


    /// If set to true the ProcessHelper will start all processes with a custom ProcessEncoding.
    /// If set to false (default) only mono processes will be changed.
    let mutable AlwaysSetProcessEncoding = false
     
    // The ProcessHelper will start all processes with this encoding if AlwaysSetProcessEncoding is set to true.
    /// If AlwaysSetProcessEncoding is set to false (default) only mono processes will be changed.
    let mutable ProcessEncoding = Encoding.UTF8

    let internal rawStartProcess (proc : Process) =
        try
            let result = proc.Start()
            if not result then failwithf "Could not start process (Start() returned false)."
        with ex -> raise <| exn(sprintf "Start of process '%s' failed." proc.StartInfo.FileName, ex)
        let startTime =
            try proc.StartTime with
            | :? System.InvalidOperationException
            | :? System.ComponentModel.Win32Exception as e ->
                let hasExited =
                    try proc.HasExited with
                    | :? System.InvalidOperationException
                    | :? System.ComponentModel.Win32Exception -> false
                if not hasExited then
                    Trace.traceFAKE "Error while retrieving StartTime of started process: %O" e
                DateTime.Now               
        addStartedProcess(proc.Id, startTime) |> ignore

    /// [omit]
    [<Obsolete("Do not use. If you have to use this, open an issue and explain why.")>]
    let startProcess (proc : Process) =
        rawStartProcess proc
        true

    /// [omit]
    //let mutable redirectOutputToTrace = false
    let private redirectOutputToTraceVar = "Fake.Core.Process.redirectOutputToTrace"
    let private tryGetRedirectOutputToTrace, _, public setRedirectOutputToTrace = 
        Fake.Core.FakeVar.defineAllowNoContext redirectOutputToTraceVar
    let getRedirectOutputToTrace () =
        match tryGetRedirectOutputToTrace() with
        | Some v -> v
        | None ->
          let shouldEnable = false
          setRedirectOutputToTrace shouldEnable
          shouldEnable

    /// [omit]
    //let mutable enableProcessTracing = true
    let private enableProcessTracingVar = "Fake.Core.Process.enableProcessTracing"
    let private getEnableProcessTracing, private removeEnableProcessTracing, public setEnableProcessTracing = 
        Fake.Core.FakeVar.defineAllowNoContext enableProcessTracingVar
    let shouldEnableProcessTracing () =
        match getEnableProcessTracing() with
        | Some v -> v
        | None ->
          Fake.Core.Context.isFakeContext()
    let defaultEnvVar = ProcStartInfoData.defaultEnvVar

    let createEnvironmentMap () = ProcStartInfoData.createEnvironmentMap()


    let inline setRedirectOutput (shouldRedirect:bool) (startInfo : ^a) =
        //let inline getEnv s = ((^a) : (member Environment : unit -> Map<string, string>) (s))
        let inline setRedirect s e = ((^a) : (member WithRedirectOutput : bool -> ^a) (s, e))
        setRedirect startInfo shouldRedirect

    let inline redirectOutput (startInfo : ^a) = setRedirectOutput true startInfo
    let inline disableRedirectOutput (startInfo : ^a) = setRedirectOutput false startInfo

    let inline setEnvironment (map:Map<string, string>) (startInfo : ^a) =
        //let inline getEnv s = ((^a) : (member Environment : unit -> Map<string, string>) (s))
        let inline setEnv s e = ((^a) : (member WithEnvironment : Map<string, string> -> ^a) (s, e))
        setEnv startInfo map
        //{ startInfo with Environment = map }

    let disableShellExecute (startInfo : ProcStartInfo) =
        { startInfo with UseShellExecute = false }

    /// Sets the given environment variable for the given startInfo.
    /// Existing values will be overriden.
    let inline setEnvironmentVariable envKey (envVar:string) (startInfo : ^a) =
        let inline getEnv s = ((^a) : (member Environment : Map<string, string>) (s))
        let inline setEnv s e = ((^a) : (member WithEnvironment : Map<string, string> -> ^a) (s, e))
        
        let env = getEnv startInfo
        env
        |> (if Environment.isWindows then
                match env |> Seq.tryFind (fun kv -> kv.Key.Equals(envKey, StringComparison.OrdinalIgnoreCase)) with
                | Some oldKey -> Map.remove oldKey.Key
                | None -> id
            else Map.remove envKey)
        |> Map.add envKey envVar
        |> setEnv startInfo

    let inline getEnvironmentVariable envKey (startInfo : ^a) =
        let inline getEnv s = ((^a) : (member Environment : Map<string, string>) (s))
        
        let env = getEnv startInfo

        if Environment.isWindows then
            env
            |> Seq.tryFind (fun kv -> kv.Key.Equals(envKey, StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun kv -> kv.Value)
        else
            env
            |> Map.tryFind envKey
        
    /// Unsets the given environment variable for the started process
    let inline removeEnvironmentVariable envKey (startInfo : ^a) =
        let inline getEnv s = ((^a) : (member Environment : Map<string, string>) (s))
        let inline setEnv s e = ((^a) : (member WithEnvironment : Map<string, string> -> ^a) (s, e))
        
        let env = getEnv startInfo
        env
        |> (if Environment.isWindows then
                match env |> Seq.tryFind (fun kv -> kv.Key.Equals(envKey, StringComparison.OrdinalIgnoreCase)) with
                | Some oldKey -> Map.remove oldKey.Key
                | None -> id
            else Map.remove envKey)
        //|> Map.remove envKey
        |> setEnv startInfo

    /// Sets the given environment variables.
    let inline setEnvironmentVariables vars (startInfo : ^a) =
        vars
        |> Seq.fold (fun state (newKey, newVar) ->
                setEnvironmentVariable newKey newVar state) startInfo

    /// Sets all current environment variables to their current values
    let inline setCurrentEnvironmentVariables (startInfo : ^a) =
        setEnvironmentVariables (Environment.environVars ()) startInfo
        |> setEnvironmentVariable defaultEnvVar defaultEnvVar


    let inline getProc config =
        let startInfo : ProcStartInfo =
            config { ProcStartInfo.Create() with UseShellExecute = false }
        let proc = new Process()
        proc.StartInfo <- startInfo.AsStartInfo
        proc
    /// Runs the given process and returns the exit code.
    /// ## Parameters
    ///
    ///  - `configProcessStartInfoF` - A function which overwrites the default ProcessStartInfo.
    ///  - `timeOut` - The timeout for the process.
    ///  - `silent` - If this flag is set then the process output is redirected to the given output functions `errorF` and `messageF`.
    ///  - `errorF` - A function which will be called with the error log.
    ///  - `messageF` - A function which will be called with the message log.
    let execRaw configProcessStartInfoF (timeOut : TimeSpan) silent errorF messageF =
        use proc = getProc configProcessStartInfoF
        
        //platformInfoAction proc.StartInfo
        if String.isNullOrEmpty proc.StartInfo.WorkingDirectory |> not then 
            if Directory.Exists proc.StartInfo.WorkingDirectory |> not then
                sprintf "Start of process '%s' failed. WorkingDir '%s' does not exist." proc.StartInfo.FileName 
                    proc.StartInfo.WorkingDirectory
                |> DirectoryNotFoundException
                |> raise
        if silent then 
            proc.StartInfo.RedirectStandardOutput <- true
            proc.StartInfo.RedirectStandardError <- true
            if Environment.isMono || AlwaysSetProcessEncoding then
                proc.StartInfo.StandardOutputEncoding <- ProcessEncoding
                proc.StartInfo.StandardErrorEncoding  <- ProcessEncoding
            proc.ErrorDataReceived.Add(fun d -> 
                if isNull d.Data |> not then errorF d.Data)
            proc.OutputDataReceived.Add(fun d -> 
                if isNull d.Data |> not then messageF d.Data)
        if shouldEnableProcessTracing() && (not <| proc.StartInfo.FileName.EndsWith "fsi.exe") then 
            Trace.tracefn "%s %s" proc.StartInfo.FileName proc.StartInfo.Arguments
        rawStartProcess proc
        if silent then 
            proc.BeginErrorReadLine()
            proc.BeginOutputReadLine()
        if timeOut = TimeSpan.MaxValue then proc.WaitForExit()
        else 
            if not <| proc.WaitForExit(int timeOut.TotalMilliseconds) then 
                try 
                    proc.Kill()
                with exn ->
                    Trace.traceError 
                    <| sprintf "Could not kill process %s  %s after timeout: %O" proc.StartInfo.FileName 
                           proc.StartInfo.Arguments exn
                failwithf "Process %s %s timed out." proc.StartInfo.FileName proc.StartInfo.Arguments
        // See http://stackoverflow.com/a/16095658/1149924 why WaitForExit must be called twice.
        proc.WaitForExit()
        proc.ExitCode

    /// Runs the given process and returns the process result.
    /// ## Parameters
    ///
    ///  - `configProcessStartInfoF` - A function which overwrites the default ProcessStartInfo.
    ///  - `timeOut` - The timeout for the process.
    let execWithResult configProcessStartInfoF timeOut = 
        let messages = ref []
        
        let appendMessage isError msg = 
            messages := { IsError = isError
                          Message = msg
                          Timestamp = DateTimeOffset.UtcNow } :: !messages
        
        let exitCode = 
            execRaw configProcessStartInfoF timeOut true (appendMessage true) (appendMessage false)
        ProcessResult.New exitCode (!messages |> List.rev)

    /// Runs the given process and returns the exit code.
    /// ## Parameters
    ///
    ///  - `configProcessStartInfoF` - A function which overwrites the default ProcessStartInfo.
    ///  - `timeOut` - The timeout for the process.
    /// ## Sample
    ///
    ///     let result = ExecProcess (fun info ->  
    ///                       info.FileName <- "c:/MyProc.exe"
    ///                       info.WorkingDirectory <- "c:/workingDirectory"
    ///                       info.Arguments <- "-v") (TimeSpan.FromMinutes 5.0)
    ///     
    ///     if result <> 0 then failwithf "MyProc.exe returned with a non-zero exit code"
    let execSimple configProcessStartInfoF timeOut = 
        execRaw configProcessStartInfoF timeOut (getRedirectOutputToTrace()) Trace.traceError Trace.trace

    // workaround to remove warning
    let private myExecElevated cmd args timeout =
    #if FX_VERB
        execSimple (fun si ->
            { si with
                Verb = "runas"
                Arguments = args
                FileName = cmd
                UseShellExecute = true }) timeout
    #else
        failwithf "Elevated processes not possible with netstandard16 build."        
    #endif

    /// Runs the given process in an elevated context and returns the exit code.
    /// ## Parameters
    ///
    ///  - `cmd` - The command which should be run in elavated context.
    ///  - `args` - The process arguments.
    ///  - `timeOut` - The timeout for the process.
    [<Obsolete("This is currently not possible in dotnetcore")>]
    let execElevated cmd args timeOut = 
        myExecElevated cmd args timeOut

    /// Starts the given process and returns immediatly.
    let fireAndForget configProcessStartInfoF =
        use proc = getProc configProcessStartInfoF
        rawStartProcess proc

    /// Runs the given process, waits for its completion and returns if it succeeded.
    let directExec configProcessStartInfoF = 
        use proc = getProc configProcessStartInfoF
        rawStartProcess proc
        proc.WaitForExit()
        proc.ExitCode = 0

    /// Starts the given process and forgets about it.
    let start configProcessStartInfoF = 
        use proc = getProc configProcessStartInfoF
        rawStartProcess proc

    /// Adds quotes around the string
    /// [omit]
    let quote (str:string) =
        // "\"" + str.Replace("\"","\\\"") + "\""
        CmdLineParsing.windowsArgvToCommandLine [ str ]

    /// Adds quotes around the string if needed
    /// [omit]
    let quoteIfNeeded str = quote str
        //if String.isNullOrEmpty str then ""
        //elif str.Contains " " then quote str
        //else str

    /// Adds quotes and a blank around the string´.
    /// [omit]
    let toParam x = " " + quoteIfNeeded x

    /// Use default Parameters
    /// [omit]
    let UseDefaults = id

    /// [omit]
    let stringParam (paramName, paramValue) = 
        if String.isNullOrEmpty paramValue then None
        else Some(paramName, paramValue)

    /// [omit]
    let multipleStringParams paramName = Seq.map (fun x -> stringParam (paramName, x)) >> Seq.toList

    /// [omit]
    let optionParam (paramName, paramValue) = 
        match paramValue with
        | Some x -> Some(paramName, x.ToString())
        | None -> None

    /// [omit]
    let boolParam (paramName, paramValue) = 
        if paramValue then Some(paramName, null)
        else None

    /// [omit]
    let parametersToString flagPrefix delimiter parameters = 
        parameters
        |> Seq.choose id
        |> Seq.collect (fun (paramName, paramValue) ->
            if String.isNullOrEmpty paramValue || delimiter <> " " then
              let delimimeterAndValue =
                  if String.isNullOrEmpty paramValue then ""
                  else delimiter + paramValue
              [ flagPrefix + paramName + delimimeterAndValue ]
            else 
              [ flagPrefix + paramName
                paramValue ])
        |> CmdLineParsing.windowsArgvToCommandLine

    /// Searches the given directories for all occurrences of the given file name
    /// [omit]
    let findFiles dirs file = 
        let files = 
            dirs
            |> Seq.map (fun (path : string) -> 
                   let dir = 
                       path
                       |> String.replace "[ProgramFiles]" Environment.ProgramFiles
                       |> String.replace "[ProgramFilesX86]" Environment.ProgramFilesX86
                       |> String.replace "[SystemRoot]" Environment.SystemRoot
                       |> DirectoryInfo.ofPath
                   if not dir.Exists then ""
                   else 
                       let fi = dir.FullName @@ file
                                |> FileInfo.ofPath
                       if fi.Exists then fi.FullName
                       else "")
            |> Seq.filter ((<>) "")
            |> Seq.cache
        files

    /// Searches the given directories for all occurrences of the given file name
    /// [omit]
    let tryFindFile dirs file =
        let files = findFiles dirs file
        if not (Seq.isEmpty files) then Some(Seq.head files)
        else None

    /// Searches the given directories for the given file, failing if not found.
    /// [omit]
    let findFile dirs file = 
        match tryFindFile dirs file with
        | Some found -> found
        | None -> failwithf "%s not found in %A." file dirs

    /// Searches in PATH for the given file and returnes the result ordered by precendence
    let findFilesOnPath (file : string) : string seq =
        Environment.pathDirectories
        |> Seq.filter Path.isValidPath
        |> Seq.append [ "." ]
        |> fun path ->
            // See https://unix.stackexchange.com/questions/280528/is-there-a-unix-equivalent-of-the-windows-environment-variable-pathext
            if Environment.isWindows then
                // Prefer PATHEXT, see https://github.com/fsharp/FAKE/issues/1911
                // and https://github.com/fsharp/FAKE/issues/1899
                Environment.environVarOrDefault "PATHEXT" ".COM;.EXE;.BAT"
                |> String.split ';'
                |> Seq.collect (fun postFix -> findFiles path (file + postFix))
                |> fun findings -> Seq.append findings (findFiles path file)
            else findFiles path file

    /// Searches the current directory and the directories within the PATH
    /// environment variable for the given file. If successful returns the full
    /// path to the file.
    /// ## Parameters
    ///  - `file` - The file to locate
    let tryFindFileOnPath (file : string) : string option =
        findFilesOnPath file |> Seq.tryHead

    /// Returns the AppSettings for the key - Splitted on ;
    /// [omit]
    [<Obsolete("This is no longer supported on dotnetcore.")>]
    let appSettings (key : string) (fallbackValue : string) = 
        let value = 
            let setting =
    #if FX_CONFIGURATION_MANAGER
                try
                    System.Configuration.ConfigurationManager.AppSettings.[key]
                with _ -> ""
    #else
                null
    #endif
            if not (String.isNullOrWhiteSpace setting) then setting
            else fallbackValue
        value.Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)

    /// Tries to find the tool via Env-Var. If no path has the right tool we are trying the PATH system variable.
    let tryFindTool envVar tool =
        match Environment.environVarOrNone envVar with
        | Some path -> Some path
        | None -> tryFindFileOnPath tool

    /// Tries to find the tool via AppSettings. If no path has the right tool we are trying the PATH system variable.
    /// [omit]
    let tryFindPath settingsName fallbackValue tool = 
        let paths = appSettings settingsName fallbackValue
        match tryFindFile paths tool with
        | Some path -> Some path
        | None -> tryFindFileOnPath tool

    /// Tries to find the tool via AppSettings. If no path has the right tool we are trying the PATH system variable.
    /// [omit]
    let findPath settingsName fallbackValue tool = 
        match tryFindPath settingsName fallbackValue tool with
        | Some file -> file
        | None -> tool

    let private formatArgs args = 
        let delimit (str : string) = 
            if String.isLetterOrDigit (str.Chars(str.Length - 1)) then str + " "
            else str
        args
        |> Seq.collect (fun (k, v) -> [ delimit k; v ])
        |> CmdLineParsing.windowsArgvToCommandLine

    /// Execute an external program asynchronously and return the exit code,
    /// logging output and error messages to FAKE output. You can compose the result
    /// with Async.Parallel to run multiple external programs at once, but be
    /// sure that none of them depend on the output of another.
    let asyncShellExec (args : ExecParams) = 
        async { 
            if String.isNullOrEmpty args.Program then invalidArg "args" "You must specify a program to run!"
            let commandLine = args.CommandLine + " " + formatArgs args.Args
            let info = 
                ProcessStartInfo
                    (args.Program, UseShellExecute = false, 
                     RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true,
    #if FX_WINDOWSTLE
                     WindowStyle = ProcessWindowStyle.Hidden,
    #else
                     CreateNoWindow = true,
    #endif
                     WorkingDirectory = args.WorkingDir, 
                     Arguments = commandLine)
            use proc = new Process(StartInfo = info)
            proc.ErrorDataReceived.Add(fun e -> 
                if not (isNull e.Data) then Trace.traceError e.Data)
            proc.OutputDataReceived.Add(fun e -> 
                if not (isNull e.Data) then Trace.log e.Data)
            rawStartProcess proc
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            proc.StandardInput.Dispose()
            // attaches handler to Exited event, enables raising events, then awaits event
            // the event gets triggered even if process has already finished
            let! _ = Async.GuardedAwaitObservable proc.Exited (fun _ -> proc.EnableRaisingEvents <- true)
            return proc.ExitCode
        }


    /// Kills all processes with the given id
    let killById id = Process.GetProcessById id |> kill
    [<System.Obsolete("use Process.killById instead.")>]
    let killProcessById id = killById id

    /// Retrieve the file-path of the running executable of the given process.
    let getFileName (p:Process) =
#if !FX_NO_HANDLE
        if Environment.isWindows then
            Kernel32.getPathToApp p
        else
#endif    
            p.MainModule.FileName

    /// Returns all processes with the given name
    let getAllByName (name : string) = 
        Process.GetProcesses()
        |> Seq.filter (fun p -> 
               try 
                   not p.HasExited
               with _ -> false)
        |> Seq.filter (fun p -> 
               try 
                   p.ProcessName.ToLower().StartsWith(name.ToLower())
               with _ -> false)

    [<System.Obsolete("use Process.getAllByName instead.")>]
    let getProcessesByName name = getAllByName name

    /// Kills all processes with the given name
    let killAllByName name = 
        Trace.tracefn "Searching for process with name = %s" name
        getAllByName name |> Seq.iter kill

    [<System.Obsolete("use Process.killAllByName instead.")>]
    let killProcess name = killAllByName name

    /// Kills the F# Interactive (FSI) process.
    let killFSI() = killAllByName "fsi.exe"

    /// Kills the MSBuild process.
    let killMSBuild() = killAllByName "msbuild"

    /// Kills all processes that are created by the FAKE build script unless "donotkill" flag was set.
    let killAllCreatedProcesses() =
        match getStartedProcesses() with
        | Some startedProcesses when shouldKillCreatedProcesses() ->
            startedProcesses.KillAll()
        | _ -> ()

    /// Waits until the processes with the given name have stopped or fails after given timeout.
    /// ## Parameters
    ///  - `name` - The name of the processes in question.
    ///  - `timeout` - The timespan to time out after.
    let ensureProcessesHaveStopped name timeout =
        let endTime = DateTime.Now.Add timeout
        while DateTime.Now <= endTime && not (getAllByName name |> Seq.isEmpty) do
            Trace.tracefn "Waiting for %s to stop (Timeout: %A)" name endTime
            Thread.Sleep 1000
        if not (getAllByName name |> Seq.isEmpty) then
            failwithf "The process %s has not stopped (check the logs for errors)" name

    /// Execute an external program and return the exit code.
    /// [omit]
    let shellExec args = args |> asyncShellExec |> Async.RunSynchronously


    let internal monoPath, monoVersion =
        match tryFindTool "MONO" "mono" with
        | Some path ->
            let result =
                try execWithResult(fun proc ->
                        { proc with
                            FileName = path
                            Arguments = "--version" }) (TimeSpan.FromMinutes 1.)
                with e ->
                    ProcessResult.New 1
                        [{ ConsoleMessage.IsError = true; ConsoleMessage.Message = e.ToString(); ConsoleMessage.Timestamp = DateTimeOffset.Now }]

            let out =
                let outStr = String.Join("\n", result.Results |> Seq.map (fun m -> m.Message))
                sprintf "Success: %b (%d), Out: %s" result.OK result.ExitCode outStr
            let ver =
                match result.OK, result.Results |> Seq.tryHead with
                | true, Some firstLine ->
                    Some (out, Environment.Internal.parseMonoDisplayName firstLine.Message)
                | _ ->
                    Some (out, None)
            Some path, ver
        | None -> None, None

    /// Ensures the executable is run with the full framework. On non-windows platforms that means running the tool by invoking 'mono'.
    let withFramework (proc:ProcStartInfo) =
        match Environment.isWindows, proc.FileName.ToLowerInvariant().EndsWith(".exe"), monoPath with
        | false, true, Some monoPath ->
            { proc with 
                Arguments =  "--debug \"" + proc.FileName + "\" " + proc.Arguments
                FileName = monoPath }
        | false, true, _ ->
            failwithf "trying to start a .NET process on a non-windows platform, but mono could not be found. Try to set the MONO environment variable or add mono to the PATH."
        | _ -> proc

    [<System.Obsolete("use Fake.Core.ProcStartInfo instead")>]
    type ProcStartInfo = Fake.Core.ProcStartInfo
    [<System.Obsolete("use Fake.Core.ExecParams instead")>]
    type ExecParams = Fake.Core.ExecParams
    [<System.Obsolete("use Fake.Core.ProcessResult instead")>]
    type ProcessResult = Fake.Core.ProcessResult
    [<System.Obsolete("use Fake.Core.ConsoleMessage instead")>]
    type ConsoleMessage = Fake.Core.ConsoleMessage

/// Allows to exec shell operations synchronously and asynchronously.
type Shell private() = 
    static member private GetParams(cmd, ?args, ?dir) = 
        let args = defaultArg args ""
        let dir = defaultArg dir (Directory.GetCurrentDirectory())
        { WorkingDir = dir
          Program = cmd
          CommandLine = args
          Args = [] }
    
    /// Runs the given process, waits for it's completion and returns the exit code.
    /// ## Parameters
    ///
    ///  - `cmd` - The command which should be run in elavated context.
    ///  - `args` - The process arguments (optional).
    ///  - `directory` - The working directory (optional).
    static member Exec(cmd, ?args, ?dir) = Process.shellExec (Shell.GetParams(cmd, ?args = args, ?dir = dir))
    
    /// Runs the given process asynchronously.
    /// ## Parameters
    ///
    ///  - `cmd` - The command which should be run in elavated context.
    ///  - `args` - The process arguments (optional).
    ///  - `directory` - The working directory (optional).
    static member AsyncExec(cmd, ?args, ?dir) = Process.asyncShellExec (Shell.GetParams(cmd, ?args = args, ?dir = dir))

[<AutoOpen>]
module ProcStartInfoExtensions =
    type ProcStartInfo with
        /// Gets or sets the set of command-line arguments to use when starting the application.
        member x.WithArguments args = { x with Arguments = args }
        /// Gets or sets a value indicating whether to start the process in a new window.
        member x.WithCreateNoWindow noWindow = { x with CreateNoWindow = noWindow }
        /// Gets or sets a value that identifies the domain to use when starting the process.
        member x.WithDomain domain = { x with Domain = domain }
        /// Remove the current Environment Variables and use the default
        member x.WithoutEnvironment () = { x with Environment = Map.empty } |> Process.setCurrentEnvironmentVariables
        /// Sets the given environment variable for the given startInfo.
        member x.WithEnvironmentVariable(envKey, envVar) =
            Process.setEnvironmentVariable envKey envVar x
        /// Unsets the given environment variable for the given startInfo.
        member x.WithRemovedEnvironmentVariable(envKey) =
            Process.removeEnvironmentVariable envKey x
        /// Gets or sets a value that identifies the domain to use when starting the process.
        member x.WithEnvironmentVariables vars =
            Process.setEnvironmentVariables vars x
        /// Sets the current environment variables.
        member x.WithCurrentEnvironmentVariables () =
            Process.setCurrentEnvironmentVariables x

#if FX_ERROR_DIALOG
        /// Gets or sets a value indicating whether an error dialog box is displayed to the user if the process cannot be started.
        member x.WithErrorDialog errorDialog = { x with ErrorDialog = errorDialog }
        /// Gets or sets the window handle to use when an error dialog box is shown for a process that cannot be started.
        member x.WithErrorDialogParentHandle handle = { x with ErrorDialogParentHandle = handle }
#endif  
        /// Gets or sets the application or document to start.
        member x.WithFileName name = { x with FileName = name }
        /// true if the Windows user profile should be loaded; otherwise, false. The default is false.
        member x.WithLoadUserProfile userProfile = { x with LoadUserProfile = userProfile }
        // Note: No SecureString as that one is obsolete anyway and to provide a uniform API across netstandard16.
        /// Gets or sets the user password in clear text to use when starting the process.
        member x.WithPassword password = { x with Password = password }
#if FX_WINDOWSTLE
        /// One of the enumeration values that indicates whether the process is started in a window that is maximized, minimized, normal (neither maximized nor minimized), or not visible. The default is Normal.
        member x.WithWindowStyle style = { x with WindowStyle = style }
#endif  
        /// true if error output should be written to Process.StandardError; otherwise, false. The default is false.
        member x.WithRedirectStandardError redirectStdErr = { x with RedirectStandardError = redirectStdErr }
        /// true if input should be read from Process.StandardInput; otherwise, false. The default is false.
        member x.WithRedirectStandardInput redirectStdInput = { x with RedirectStandardInput = redirectStdInput }
        /// true if output should be written to Process.StandardOutput; otherwise, false. The default is false.
        member x.WithRedirectStandardOutput redirectStdOutput = { x with RedirectStandardOutput = redirectStdOutput }
        /// An object that represents the preferred encoding for error output. The default is null.
        member x.WithStandardErrorEncoding encoding = { x with StandardErrorEncoding = encoding }
        /// An object that represents the preferred encoding for standard output. The default is null.
        member x.WithStandardOutputEncoding encoding = { x with StandardOutputEncoding = encoding }
        /// The user name to use when starting the process. If you use the UPN format, user@DNS_domain_name, the Domain property must be null.
        member x.WithUserName name = { x with UserName = name }
        /// true if the shell should be used when starting the process; false if the process should be created directly from the executable file. The default is true.
        member x.WithUseShellExecute shellExec = { x with UseShellExecute = shellExec }
#if FX_VERB
        /// The action to take with the file that the process opens. The default is an empty string (""), which signifies no action.
        member x.WithVerb name = { x with Verb = name }
#endif
        /// When UseShellExecute is true, the fully qualified name of the directory that contains the process to be started. When the UseShellExecute property is false, the working directory for the process to be started. The default is an empty string ("").
        member x.WithWorkingDirectory dir = { x with WorkingDirectory = dir }
