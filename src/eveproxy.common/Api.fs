namespace eveproxy

open System
open Giraffe
open Microsoft.AspNetCore.Http

module Api =

    let pickHeaders allowedHeaders (headers: HttpResponseHeaders) : HttpResponseHeaders =
        let contains values value =
            values
            |> Seq.exists (fun v -> StringComparer.InvariantCultureIgnoreCase.Equals(v, value))

        headers |> List.filter (fst >> (contains allowedHeaders))

    let contentString (contentType: string) (headers: HttpResponseHeaders) (value: string) : HttpHandler =
        let bytes = System.Text.Encoding.UTF8.GetBytes value

        let rec appendHeaders (ctx: HttpContext) headers =
            match headers with
            | [] -> ignore 0
            | h :: t ->
                let (k, v) = h
                ctx.SetHttpHeader(k, v)
                appendHeaders ctx t

        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {

                headers |> appendHeaders ctx

                ctx.SetContentType contentType

                let! _ = ctx.WriteBytesAsync bytes

                return! next ctx
            }

    let jsonString headers (value: string) =
        value |> contentString "application/json; charset=utf-8" headers
