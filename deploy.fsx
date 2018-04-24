#r "packages/build/FAKE/tools/FakeLib.dll"
#r "netstandard"
#I "packages/build/Microsoft.Rest.ClientRuntime.Azure/lib/net452"
#load ".paket/load/netcoreapp2.1/Build/build.group.fsx"
#load @"paket-files\build\CompositionalIT\fshelpers\src\FsHelpers\ArmHelper\ArmHelper.fs"

open Cit.Helpers.Arm
open Cit.Helpers.Arm.Parameters
open Microsoft.Azure.Management.ResourceManager.Fluent.Core
open Fake
open System

// Publish

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let serverPath = "./src/Server" |> FullName
let clientPath = "./src/Client" |> FullName
let publicPath = clientPath </> "public"
let deployDir = "./deploy" |> FullName
let deployPublicPath = deployDir </> "public"

let platformTool tool winTool =
  let tool = if isUnix then tool else winTool
  tryFindFileOnPath tool |> Option.defaultWith (fun () -> failwithf "%s not found" tool)

let run cmd args workingDir =
  let result =
    ExecProcess (fun info ->
      info.FileName <- cmd
      info.WorkingDirectory <- workingDir
      info.Arguments <- args) TimeSpan.MaxValue
  if result <> 0 then failwithf "'%s %s' failed" cmd args

Target "Publish" (fun () ->
  CleanDirs [deployDir]
  let yarn = platformTool "yarn" "yarn.cmd"
  run yarn "install --frozen-lockfile" __SOURCE_DIRECTORY__
  run "dotnet" (sprintf "publish %s -c release -o %s" serverPath deployDir) __SOURCE_DIRECTORY__
  run "dotnet" "fable webpack -- -p" clientPath
  CopyDir deployPublicPath publicPath allFiles
)

// Deploy

type ArmOutput =
  { WebAppName : ParameterValue<string>
    WebAppPassword : ParameterValue<string> }
let environment = getBuildParamOrDefault "environment" (Guid.NewGuid().ToString().ToLower().Split '-' |> Array.head)
let subscriptionId = getBuildParam "subscriptionId"
let clientId = getBuildParam "clientId"

let mutable deploymentOutputs : ArmOutput option = None

Target "DeployArmTemplate" (fun _ ->
  let armTemplate = @"arm-template.json"
  let resourceGroupName = "safe-" + environment
  let subscriptionId = Guid.Parse subscriptionId
  let clientId = Guid.Parse clientId

  tracefn "Deploying template '%s' to resource group '%s' in subscription '%O'..." armTemplate resourceGroupName subscriptionId

  let authCtx =
    subscriptionId
    |> authenticateDevice trace { ClientId = clientId; TenantId = None }
    |> Async.RunSynchronously

  let deployment =
     { DeploymentName = "fake-deploy"
       ResourceGroup = New(resourceGroupName, Region.EuropeWest)
       ArmTemplate = IO.File.ReadAllText armTemplate
       Parameters = Simple [ "environment", ArmString environment ]
       DeploymentMode = Incremental }

  deployment
  |> deployWithProgress authCtx
  |> Seq.iter(function
    | DeploymentInProgress (state, operations) -> tracefn "State is %s, completed %d operations." state operations
    | DeploymentError (statusCode, message) -> traceError <| sprintf "DEPLOYMENT ERROR: %s - '%s'" statusCode message
    | DeploymentCompleted d -> deploymentOutputs <- d)
)

Target "DeployWebApp" (fun _ ->
  let zipFile = "deploy.zip"
  IO.File.Delete zipFile
  Zip deployDir zipFile !!(deployDir + @"\**\**")
  
  let appName = deploymentOutputs.Value.WebAppName.value
  let appPassword = deploymentOutputs.Value.WebAppPassword.value
  let destinationUri = sprintf "https://%s.scm.azurewebsites.net/api/zipdeploy" appName
  tracefn "Uploading %s to %s" zipFile destinationUri
  let client = new Net.WebClient(Credentials = Net.NetworkCredential("$" + appName, appPassword))
  client.UploadData(destinationUri, IO.File.ReadAllBytes zipFile) |> ignore
)

"Publish" ==> "DeployWebApp"
"DeployArmTemplate" ==> "DeployWebApp"

RunTargetOrDefault "DeployWebApp"