namespace Giraffe

module ResponseWriters =
  open Microsoft.AspNetCore.Http
  open FSharp.Control.Tasks.V2
  open Microsoft.Net.Http.Headers
  open System.Threading.Tasks

  type HttpContext with
    member this.WriteStreamAsync (s: System.IO.Stream) = task {
      use stream = s
      this.SetHttpHeader HeaderNames.ContentLength s.Length
      do! stream.CopyToAsync(this.Response.Body)
      return Some this
    }

  let setBodyFromStream (s: System.IO.Stream): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) -> ctx.WriteStreamAsync s

