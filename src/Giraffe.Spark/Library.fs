namespace Giraffe.Spark

[<RequireQualifiedAccess>]
module SparkEngine =
  open Spark
  open Microsoft.AspNetCore.Http
  open System.IO
  open System.Reflection
  open Microsoft.Extensions.Logging

  type ViewError = | Generic of exn

  let locateViews (engine: ISparkViewEngine): ISparkViewEngine =
    let targetDll = Assembly.GetExecutingAssembly().Location

    engine

  let tryMakeDescriptorForView (viewFolder: FileSystem.IViewFolder) viewName =
    [ viewName
      Path.ChangeExtension(viewName, ".shade")
      Path.ChangeExtension(viewName, ".spark") ]
    |> List.tryFind (fun viewName -> viewFolder.HasView(viewName))
    |> Option.map (fun foundView -> SparkViewDescriptor().AddTemplate(foundView))

  let tryFindView (engine: ISparkViewEngine) (ctx: HttpContext) (viewName: string) (model: obj) : Result<ISparkViewEntry, ViewError> =
    match tryMakeDescriptorForView engine.ViewFolder viewName with
    | Some descriptor -> Ok <| engine.CreateEntry(descriptor)
    | None -> Error <| Generic (failwith "not implemented")

  let renderView (engine: ISparkViewEngine) (ctx: HttpContext) (viewName: string) (model: 't): Result<Stream, ViewError> =
    match tryFindView engine ctx viewName (box model) with
    | Ok viewEntry ->
      let stream = new MemoryStream() :> Stream
      let writer = new StreamWriter(stream) :> TextWriter
      let instance = viewEntry.CreateInstance()
      let instanceTy = instance.GetType()
      try
        let setter = instanceTy.GetProperty("Model")
        setter.SetValue(instance, box model)
      with e ->
        let logger = ctx.RequestServices.GetService(typeof<ILogger<obj>>) :?> ILogger<obj>
        logger.LogWarning("unable to set model on page type. is your base page type exposing a public model property of type object?")
      instance.RenderView(writer)
      stream.Position <- 0L
      Ok stream
    | Error e -> Error e

[<AutoOpen>]
module Middleware =
  open Microsoft.Extensions.DependencyInjection
  open Spark
  open Spark.FileSystem

  type IServiceCollection with

    /// **Description**
    /// Configures the Spark View Engine given a user-defined dleegate.
    ///
    /// **Parameters**
    ///   * `configureSparkSettings` - A configuration function for a `SparkSettings` object
    ///
    /// **Output Type**
    ///   * `IServiceCollection`
    ///
    /// **Exceptions**
    ///
    member this.AddSparkViewEngine (configureSparkSettings: SparkSettings -> SparkSettings): IServiceCollection =
      let settings = SparkSettings () |> configureSparkSettings
      let engine = new SparkViewEngine(settings) |> SparkEngine.locateViews
      // TODO: find and compile all the views in the engine's view folders
      this
        .AddAntiforgery()
        .AddSingleton<ISparkViewEngine>(engine)

    /// **Description**
    /// Configures the Spark View Engine to use a given folder of Views, and defaults to the Spark.AbstractSparkView as the base page type.
    ///
    /// **Parameters**
    ///   * `viewsFolderPath` - The root folder path of the Spark/Shade Views
    ///
    /// **Output Type**
    ///   * `IServiceCollection`
    ///
    /// **Exceptions**
    ///
    member this.AddSparkViewEngine (viewsFolderPath: string) =
      this.AddSparkViewEngine (fun s -> s.SetPageBaseType(typeof<AbstractSparkView>).AddViewFolder(ViewFolderType.FileSystem, dict [ "basePath", viewsFolderPath ]))

    /// **Description**
    /// Configures the Spark View Engine to use a given folder of Views, as well as a given base type for the view pages.
    ///
    /// **Parameters**
    ///   * `viewsFolderPath` - The root folder path of the Spark/Shade Views
    ///   * `baseViewType` - The base Type of your views. This type will be created via reflection and any members on it accessible in your views
    ///
    /// **Output Type**
    ///   * `IServiceCollection`
    ///
    /// **Exceptions**
    ///
    member this.AddSparkViewEngine (viewsFolderPath: string, baseViewType: System.Type) =
      this.AddSparkViewEngine (fun s -> s.SetPageBaseType(baseViewType).AddViewFolder(ViewFolderType.FileSystem, dict [ "basePath", viewsFolderPath ]))

[<AutoOpen>]
module HttpHandlers =
  open Giraffe
  open Giraffe.ResponseWriters
  open FSharp.Control.Tasks.V2
  open System.Threading.Tasks

  open Microsoft.AspNetCore.Antiforgery
  open Microsoft.AspNetCore.Http
  open Microsoft.Extensions.DependencyInjection

  open Spark

  type Marker = | Marker

  let sparkView (contentType: string) (viewName: string) (model: 't): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) -> task {
      let engine = ctx.RequestServices.GetService<ISparkViewEngine>()
      match SparkEngine.renderView engine ctx viewName model with
      | Error e ->
        return! failwith (sprintf "Error rendering view %s: %A" viewName e)
      | Ok stream ->
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

