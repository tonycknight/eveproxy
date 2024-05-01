﻿namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open System.Net
open System.Net.Http

type HttpResponseHeaders = (string * string) list


type HttpRequestResponse =
    | HttpOkRequestResponse of
        status: HttpStatusCode *
        body: string *
        contentType: string option *
        headers: HttpResponseHeaders
    | HttpTooManyRequestsResponse of status: HttpStatusCode * headers: HttpResponseHeaders
    | HttpErrorRequestResponse of status: HttpStatusCode * body: string * headers: HttpResponseHeaders
    | HttpExceptionRequestResponse of ex: Exception
    // TODO: | HttpBadGatewayResponse of status: HttpStatusCode
    
    static member status(response: HttpRequestResponse) =
        match response with
        | HttpOkRequestResponse(status, _, _, _) -> status
        | HttpTooManyRequestsResponse(status, _) -> status
        | HttpErrorRequestResponse(status, _, _) -> status
        | HttpExceptionRequestResponse _ -> HttpStatusCode.InternalServerError

    static member loggable(response: HttpRequestResponse) =
        let status = HttpRequestResponse.status response
        $"{response.GetType().Name} {status}"

    static member headers(response: HttpRequestResponse) =
        match response with
        | HttpOkRequestResponse(_, _, _, headers) -> headers
        | HttpTooManyRequestsResponse(_, headers) -> headers
        | HttpErrorRequestResponse(_, _, headers) -> headers
        | _ -> []

    static member headerValues name (response: HttpRequestResponse) =
        response
        |> HttpRequestResponse.headers
        |> Seq.filter (fun t -> StringComparer.InvariantCultureIgnoreCase.Equals(fst t, name))
        |> Seq.map snd

[<ExcludeFromCodeCoverage>]
module Http =

    let body (resp: HttpResponseMessage) =
        task {
            let! body =
                match resp.Content.Headers.ContentEncoding |> Seq.tryHead with
                | Some x when x = "gzip" ->
                    task {
                        use s = resp.Content.ReadAsStream(System.Threading.CancellationToken.None)
                        return Strings.fromGzip s
                    }
                | _ -> resp.Content.ReadAsStringAsync()

            return body
        }

    let headers (resp: HttpResponseMessage) =
        resp.Headers
        |> Seq.collect (fun x -> x.Value |> Seq.map (fun v -> (Strings.toLower x.Key, v)))
        |> List.ofSeq

    let parse (resp: HttpResponseMessage) =
        let respHeaders = headers resp

        match resp.IsSuccessStatusCode, resp.StatusCode with
        | true, _ ->
            task {
                let! body = body resp

                let mediaType =
                    resp.Content.Headers.ContentType
                    |> Option.ofNull<Headers.MediaTypeHeaderValue>
                    |> Option.map _.MediaType

                return HttpOkRequestResponse(resp.StatusCode, body, mediaType, respHeaders)
            }
        | false, HttpStatusCode.TooManyRequests ->
            HttpTooManyRequestsResponse(resp.StatusCode, respHeaders)
            |> eveproxy.Threading.toTaskResult
        | false, _ ->
            task {
                let! body = body resp
                return HttpErrorRequestResponse(resp.StatusCode, body, respHeaders)
            }

    let send (client: HttpClient) (msg: HttpRequestMessage) =
        task {
            try
                let! resp = client.SendAsync msg
                return! parse resp
            with ex ->
                return HttpExceptionRequestResponse(ex)
        }

type IInternalHttpClient =
    abstract member GetAsync: url: string -> Task<HttpRequestResponse>
    abstract member PutAsync: url: string -> content: string -> Task<HttpRequestResponse>

[<ExcludeFromCodeCoverage>]
type InternalHttpClient(httpClient: HttpClient, config: AppConfiguration, secrets: IKeyValueProvider) =
    let httpSend = Http.send httpClient

    let appendApiKey (req: HttpRequestMessage) =
        let key = secrets.GetValue "apikey" |> Option.defaultValue ""
        req.Headers.Add("x-api-key", key)
        req

    let getReq (url: string) =
        let result = new HttpRequestMessage(HttpMethod.Get, url)
        result |> appendApiKey

    let putJsonReq (url: string) (content: string) =
        let result = new HttpRequestMessage(HttpMethod.Put, url)

        result.Content <-
            new System.Net.Http.StringContent(
                content,
                Text.Encoding.UTF8,
                System.Net.Mime.MediaTypeNames.Application.Json
            )

        result |> appendApiKey

    interface IInternalHttpClient with
        member this.GetAsync url = url |> getReq |> httpSend
        member this.PutAsync url content = content |> putJsonReq url |> httpSend


type IExternalHttpClient =
    abstract member GetAsync: url: string -> Task<HttpRequestResponse>

[<ExcludeFromCodeCoverage>]
type ExternalHttpClient(httpClient: HttpClient, config: AppConfiguration) =
    let httpSend = Http.send httpClient

    let req (url: string) =
        let req = new HttpRequestMessage(HttpMethod.Get, url)
        req.Headers.Add("User-Agent", "eveproxy")
        req.Headers.Add("Accept-Encoding", "gzip")
        req

    interface IExternalHttpClient with
        member this.GetAsync(url) = url |> req |> httpSend
