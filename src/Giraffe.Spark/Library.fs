namespace Giraffe.Spark

[<RequireQualifiedAccess>]
module SparkEngine =
  open Spark
  open Microsoft.AspNetCore.Http
  open System.IO
  open System.Reflection

  type ViewError = | Generic of exn

  let compileViews (engine: ISparkViewEngine): ISparkViewEngine =
    let targetDll = Assembly.GetExecutingAssembly().Location

    engine

  let tryFindView (engine: ISparkViewEngine) (ctx: HttpContext) (viewName: string) (model: obj) : Result<ISparkView, ViewError> =
    Error <| Generic (failwith "not implemented")

  let renderView (engine: ISparkViewEngine) (ctx: HttpContext) (viewName: string) (model: 't): Result<Stream, ViewError> =
    match tryFindView engine ctx viewName (box model) with
    | Ok view ->
      let stream = new MemoryStream() :> Stream
      use writer = new StreamWriter(stream) :> TextWriter
      view.RenderView(writer)
      stream.Position <- 0L
      Ok stream
    | Error e -> Error e

[<AutoOpen>]
module Middleware =
  open Microsoft.Extensions.DependencyInjection
  open Spark
  open Spark.FileSystem

  type IServiceCollection with
    member this.AddSparkViewEngine (configureSparkSettings: SparkSettings -> SparkSettings): IServiceCollection =
      let settings = SparkSettings () |> configureSparkSettings
      let engine = new SparkViewEngine(settings) |> SparkEngine.compileViews
      // TODO: find and compile all the views in the engine's view folders
      this
        .AddAntiforgery()
        .AddSingleton<ISparkViewEngine>(engine)

    member this.AddSparkViewEngine (viewsFolderPath: string) =
      this.AddSparkViewEngine (fun s -> s.SetPageBaseType(typeof<AbstractSparkView>).AddViewFolder(ViewFolderType.FileSystem, dict [ "basePath", viewsFolderPath ]))

[<AutoOpen>]
module HttpHandlers =
  open Giraffe
  open Giraffe.ResponseWriters
  open FSharp.Control.Tasks.V2
  open System.Threading.Tasks

  open Microsoft.AspNetCore.Antiforgery
  open Microsoft.AspNetCore.Http
  open Microsoft.Extensions.DependencyInjection
  open Microsoft.Extensions.Logging

  open Spark

  type Marker = | Marker

  let sparkView (contentType: string) (viewName: string) (model: 't): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) -> task {
      let engine = ctx.RequestServices.GetService<ISparkViewEngine>()
      let logger = ctx.RequestServices.GetService<ILogger<Marker>>()
      logger.LogInformation("Beginning to render view {viewName}", viewName)
      match SparkEngine.renderView engine ctx viewName model with
      | Error e ->
        logger.LogError("Error while logging: {error}", e)
        return! failwith (sprintf "Error rendering view %s: %A" viewName e)
      | Ok stream ->
        logger.LogInformation("rendered template successfully")
        return! (setHttpHeader "Content-Type" contentType
                 >=> setBodyFromStream stream) next ctx
    }

  let sparkHtmlView (viewName: string) (model: 't): HttpHandler = sparkView "text/html" viewName model

  let validateAntiforgeryToken (invalidTokenHandler: HttpHandler): HttpHandler =
    fun next ctx -> task {
      let antiforgery = ctx.GetService<IAntiforgery>()
      let! isValid = antiforgery.IsRequestValidAsync ctx
      match isValid with
      | true -> return! next ctx
      | false -> return! invalidTokenHandler (Some >> Task.FromResult) ctx
    }

