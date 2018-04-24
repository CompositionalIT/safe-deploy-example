#r "packages/build/FAKE/tools/FakeLib.dll"

open Fake
open System

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let serverPath = "./src/Server" |> FullName
let clientPath = "./src/Client" |> FullName

let platformTool tool winTool =
  let tool = if isUnix then tool else winTool
  tryFindFileOnPath tool |> Option.defaultWith (fun () -> failwithf "%s not found" tool)

let yarnTool = platformTool "yarn" "yarn.cmd"

let dotnetcliVersion = DotNetCli.GetDotNetSDKVersionFromGlobalJson()
let mutable dotnetCli = "dotnet"

let run cmd args workingDir =
  let result =
    ExecProcess (fun info ->
      info.FileName <- cmd
      info.WorkingDirectory <- workingDir
      info.Arguments <- args) TimeSpan.MaxValue
  if result <> 0 then failwithf "'%s %s' failed" cmd args

Target "InstallDotNetCore" (fun _ ->
  dotnetCli <- DotNetCli.InstallDotNetSDK dotnetcliVersion
)

Target "InstallClient" (fun _ ->
  run yarnTool "install --frozen-lockfile" __SOURCE_DIRECTORY__
  run dotnetCli "restore" clientPath
)

Target "RestoreServer" (fun () -> 
  run dotnetCli "restore" serverPath
)

Target "Build" (fun () ->
  run dotnetCli (sprintf "build --no-incremental %s" serverPath) __SOURCE_DIRECTORY__
  run dotnetCli "fable webpack -- -p" clientPath
)

Target "Run" (fun () ->
  let server = async { run dotnetCli "watch run" serverPath }
  let client = async { run dotnetCli "fable webpack-dev-server" clientPath }
  let browser = async {
    do! Async.Sleep 5000
    Diagnostics.Process.Start "http://localhost:8080" |> ignore
  }

  [ server; client; browser]
  |> Async.Parallel
  |> Async.RunSynchronously
  |> ignore
)

"InstallDotNetCore"
  ==> "InstallClient"
  ==> "Build"

"InstallClient"
  ==> "RestoreServer"
  ==> "Run"

RunTargetOrDefault "Build"