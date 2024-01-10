﻿namespace eveproxy

open System
open System.Diagnostics.CodeAnalysis
open System.Threading.Tasks
open System.Net
open System.Net.Http

type HttpRequestResponse =
    | HttpOkRequestResponse of status: HttpStatusCode * body: string
    | HttpTooManyRequestsResponse of status: HttpStatusCode
    | HttpErrorRequestResponse of status: HttpStatusCode * body: string
    | HttpExceptionRequestResponse of ex: Exception

[<ExcludeFromCodeCoverage>]
module Uri =
    let tryParse (uri: string) =
        match Uri.IsWellFormedUriString(uri, UriKind.Absolute) with
        | true -> new Uri(uri) |> Some
        | _ -> None

[<ExcludeFromCodeCoverage>]
module Http =

    let parse (resp: HttpResponseMessage) =
        match resp.IsSuccessStatusCode with
        | true ->
            task {
                let! body = resp.Content.ReadAsStringAsync()
                return HttpOkRequestResponse(resp.StatusCode, body)
            }
        | false ->
            task {
                let! body = resp.Content.ReadAsStringAsync()
                return HttpErrorRequestResponse(resp.StatusCode, body)
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
type InternalHttpClient(httpClient: HttpClient, config: AppConfiguration, secrets: ISecretProvider) =
    let httpSend = Http.send httpClient

    let appendApiKey (req: HttpRequestMessage) =
        req.Headers.Add("x-api-key", secrets.GetSecretValue "apikey")
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
        req

    interface IExternalHttpClient with
        member this.GetAsync(url) = url |> req |> httpSend
