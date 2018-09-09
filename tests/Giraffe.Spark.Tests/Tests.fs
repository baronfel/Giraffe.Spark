module Tests


module WebApp =
  open Spark
  open Giraffe
  open System.IO
  open Microsoft.AspNetCore.Builder
  open Microsoft.AspNetCore.Hosting
  open Microsoft.Extensions.Logging
  open Microsoft.Extensions.DependencyInjection
  open Giraffe.Spark

  let app = GET >=> route "/index" >=> sparkHtmlView "index.shade" ()

  let contentRoot = Directory.GetCurrentDirectory()
  let views = Path.Combine(contentRoot, "Views")
  let builder =
    WebHostBuilder()
      .UseKestrel()
      .UseContentRoot(contentRoot)
      .Configure(fun c -> c.UseGiraffe(app))
      .ConfigureServices(fun c -> c.AddSparkViewEngine(views) |> ignore)

open Expecto
open Microsoft.AspNetCore.TestHost

[<Tests>]
let tests =
  testList "samples" [
    test "can get a rendered template" {
      use host = new TestServer(WebApp.builder)
      let call = host.CreateRequest("/index").GetAsync()
      do Async.Sleep 2000 |> Async.RunSynchronously
      Expect.isTrue call.IsCompleted (sprintf "Call errored with %A" call.Exception)
      let req = call.Result
      Expect.equal req.StatusCode System.Net.HttpStatusCode.OK "should get ok"
      let body = req.Content.ReadAsStringAsync().Result
      Expect.equal body "Hello from Spark View Engine!" "should render view"
    }
  ]

