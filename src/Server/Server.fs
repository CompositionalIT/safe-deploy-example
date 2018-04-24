open System.IO
open System.Threading.Tasks

open Giraffe
open Saturn

open Giraffe.Serialization
open Microsoft.Extensions.DependencyInjection

open Shared

let staticRootPath =
    [ Path.GetFullPath "public"; Path.GetFullPath "../Client/public" ]
    |> List.tryFind Directory.Exists
    |> Option.defaultWith (fun () -> failwith "Public content folder not found.")

let port = 8085us

let getInitCounter () : Task<Counter> = task { return 42 }

let browserRouter = scope {
  get "/" (htmlFile (Path.Combine(staticRootPath, "index.html")))
}

let config (services:IServiceCollection) =
  let fableJsonSettings = Newtonsoft.Json.JsonSerializerSettings()
  fableJsonSettings.Converters.Add(Fable.JsonConverter())
  services.AddSingleton<IJsonSerializer>(NewtonsoftJsonSerializer fableJsonSettings) |> ignore
  services
let apiRouter = scope {
  get "/init" (fun next ctx ->
    task {
      let! counter = getInitCounter()
      return! Successful.OK counter next ctx
    })
}

let mainRouter = scope {
  forward "" browserRouter
  forward "/api" apiRouter
}

let app = application {
    router mainRouter
    url ("http://0.0.0.0:" + port.ToString() + "/")
    memory_cache
    use_static staticRootPath
    service_config config
    use_gzip
}

run app