namespace Giraffe

module ResponseWriters =
  open Microsoft.AspNetCore.Http
  open FSharp.Control.Tasks.V2
  open Microsoft.Net.Http.Headers

  let setBodyFromStream (s: #System.IO.Stream): HttpHandler = fun (next: HttpFunc) (ctx: HttpContext) -> task {
    use stream = s
    ctx.SetHttpHeader HeaderNames.ContentLength s.Length
    do! stream.CopyToAsync(ctx.Response.Body)
    return! next ctx
  }
