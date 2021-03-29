module App

open Elmish
open Elmish.React
open Fable.MaterialUI.Icons
open Fable.React
open Feliz
open Feliz.MaterialUI
open Feliz.prop
open Feliz.Router
open Fulma
open Utils

open AppState
open Checkface
open Share
open Config


let parseUrl (path, query) =
    {
        FromValue = Map.tryFind "from_value" query
        ToValue = Map.tryFind "to_value" query
        FromGuid = Map.tryFind "from_guid" query |> Option.bind (System.Guid.TryParse >> tryToOption)
        ToGuid = Map.tryFind "to_guid" query |> Option.bind (System.Guid.TryParse >> tryToOption)
        FromSeed = Map.tryFind "from_seed" query |> Option.bind (System.UInt32.TryParse >> tryToOption)
        ToSeed = Map.tryFind "to_seed" query |> Option.bind (System.UInt32.TryParse >> tryToOption)
    }

let formatPathForVidValues vidValues =
    Feliz.Router.Router.formatPath("/",
        [ 
            match vidValues with
            | Some (fromValue, toValue) ->
                match fromValue with
                | CheckfaceValue value ->
                    "from_value", value
                | Guid guid ->
                    "from_guid", guid.ToString()
                | Seed seed ->
                    "from_seed", seed.ToString()

                match toValue with
                | CheckfaceValue value ->
                    "to_value", value
                | Guid guid ->
                    "to_guid", guid.ToString()
                | Seed seed ->
                    "to_seed", seed.ToString()
            | None ->
                ()
        ])

let canonicalUrl state =
    canonicalBaseUrl + formatPathForVidValues state.VidValues

let pageTitle vidValues =
    match vidValues with
    | Some (fromValue, toValue) ->
        sprintf "%s to %s" (shortCheckfaceSrcDesc fromValue) (shortCheckfaceSrcDesc toValue)
    | None ->
        siteName

let pageDescription = function
    | Some (fromValue, toValue) ->
            sprintf "Morph generated faces from %s to %s" (shortCheckfaceSrcDesc fromValue) (shortCheckfaceSrcDesc toValue)
    | None ->
            "Generate faces on the fly and morph between them"

let getCurrentPath _ =
    Browser.Dom.window.location.pathname, Browser.Dom.window.location.search

let parseSegments (pathName, queryString) =
    let urlSegments = Router.urlSegments pathName RouteMode.Path
    let urlParams =
        (Router.createUrlSearchParams queryString).entries()
        |> Seq.map (fun arr -> (arr.[0], arr.[1]))
        |> Map.ofSeq
    urlSegments, urlParams

let chooseCheckfaceSrc value guid seed =
    match value, guid, seed with
    | Some value, _, _ -> Some (CheckfaceValue value)
    | None, Some guid, _ -> Some (Guid guid)
    | None, None, Some seed -> Some (Seed seed)
    | None, None, None -> None

let urlFromCheckfaceSrc urlState =
    chooseCheckfaceSrc urlState.FromValue urlState.FromGuid urlState.FromSeed

let urlToCheckfaceSrc urlState =
    chooseCheckfaceSrc urlState.ToValue urlState.ToGuid urlState.ToSeed

let defaultFromValue = Option.defaultValue (CheckfaceValue defaultTextValue)
let defaultToValue = Option.defaultWith (fun () -> CheckfaceValue (System.DateTime.Today.ToString("yyyy-MM-dd")))

let initByUrlstate urlState =
    let fromValue = urlFromCheckfaceSrc urlState
    let toValue = urlToCheckfaceSrc urlState
    let vidValues = Option.map2 (fun a b -> a,b) fromValue toValue
    {
        LeftValue = defaultFromValue fromValue
        RightValue = defaultToValue toValue
        UploadDialogSide = None
        VidValues = vidValues
        ShareOpen = false
        ShareLinkMsg = None
        IsMorphLoading = false
        UseSlider = false
    }
let initByUrl = parseSegments >> parseUrl >> initByUrlstate
let init() = getCurrentPath() |> initByUrl, Cmd.none

let updateShare msg state =
    match msg with
    | OpenShare ->
        { state with ShareOpen = true }, Cmd.none
    | CloseShare ->
        { state with ShareOpen = false }, Cmd.none
    | CopyLink ->
        gtagEvent "CopyLink" "Share"

        state,
        match clipboard with
        | Some clipboard ->
            Cmd.OfPromise.either
                <| clipboard.writeText
                <| canonicalUrl state
                <| fun _ -> ShareMsg (SetShareMsg "Copied link to clipboard!")
                <| fun _ -> ShareMsg (SetShareMsg "Failed to copy to clipboard 😢")
        | None -> Cmd.ofMsg (ShareMsg (SetShareMsg "Failed to copy to clipboard 😢"))

    | NavigatorShare ->
        gtagEvent "NavigatorShareOpen" "Share"

        state,
        match navigatorCanShare, state.VidValues with
        | true, None -> 
            Cmd.OfPromise.result (promise {
                ignore <| navigatorShare (canonicalUrl state) (pageTitle state.VidValues) (pageDescription state.VidValues)
                return ShareMsg (SetShareMsg "Sharing page!")
            })
        | true, Some vidValues ->
            Cmd.OfPromise.result (promise {
                let fileShare = {
                    FileName = pageTitle state.VidValues + ".mp4"
                    Title = pageTitle state.VidValues
                    FileUrl = vidSrc videoDim vidValues
                    Text = (pageDescription state.VidValues)
                    Url = (canonicalUrl state)
                    ContentType = "video/mp4"
                }
                let! couldShareFile = shareFileFromUrl fileShare
                if couldShareFile then
                    return ShareMsg (SetShareMsg "Sharing morph!")
                else
                    return ShareMsg CopyLink
            })
        | false, _ ->
            Cmd.ofMsg (ShareMsg CopyLink)

    | SetShareMsg msg ->
        { state with ShareLinkMsg = Some msg },
        Cmd.OfPromise.result (promise {
            do! Promise.sleep 1000
            return ShareMsg ResetShareMsg
        })
       
    | ResetShareMsg ->
        { state with ShareLinkMsg = None }, Cmd.none

let update msg state : State * Cmd<Msg> =
    match msg with
    | SetLeftValue value ->
        { state with LeftValue = value }, Cmd.none
    | SetRightValue value ->
        { state with RightValue = value }, Cmd.none
    | ClickUploadRealImage side ->
        { state with UploadDialogSide = Some side }, Cmd.none
    | CloseUploadDialog ->
        { state with UploadDialogSide = None }, Cmd.none
    | ImageEncoded guid ->
        let leftValue, rightValue =
            match state with
            | { UploadDialogSide = Some Right; LeftValue = lVal } -> lVal, Guid guid
            | { RightValue = rVal } -> Guid guid, rVal //default to setting left side is side is not set
        { state with UploadDialogSide = None; LeftValue = leftValue; RightValue = rightValue }, Cmd.none
    | BrowseCheckfaceValues side ->
        Browser.Dom.window.location.href <- "https://names.facemorph.me"
        state, Cmd.none
    | MakeVid when state.VidValues = Some (state.LeftValue, state.RightValue) ->
        state, Cmd.none
    | MakeVid ->
        gtagEvent "MakeVid" "Video"
        state, Some (state.LeftValue, state.RightValue) |> formatPathForVidValues |> Cmd.navigatePath
    | UrlChanged (path, query) ->
        let urlState = parseUrl (path, query)
        let urlFromSrc = urlFromCheckfaceSrc urlState
        let urlToSrc = urlToCheckfaceSrc urlState
        let vidValues = Option.map2 (fun a b -> a,b) urlFromSrc urlToSrc
        //changed and has something to be loading
        let isLoading = vidValues <> state.VidValues && vidValues.IsSome
        {
            state with
                LeftValue = defaultFromValue urlFromSrc
                RightValue = defaultToValue urlToSrc
                VidValues = vidValues
                ShareOpen = false //always reset share state
                ShareLinkMsg = None
                IsMorphLoading = isLoading //set loading when has something to load
        }, Cmd.none
    | ShareMsg msg ->
        updateShare msg state
    | MorphLoaded ->
        { state with IsMorphLoading = false }, Cmd.none
    | SetUseSlider v ->
        { state with UseSlider = v }, Cmd.none

let logo : ReactElementType = Fable.Core.JsInterop.importDefault "./public/logo.svg"
let animatedLogo : ReactElementType = Fable.Core.JsInterop.importDefault "./public/logo-animated.svg"

let getShareState state =
    { 
        IsOpen = state.ShareOpen
        LinkMsg = state.ShareLinkMsg
        CanonicalUrl = canonicalUrl state
        IsShown = state.VidValues.IsSome
    }

let header =
    Html.header [
        prop.style [ style.marginBottom (length.em 2) ]
        prop.children [
            Column.column [ ] [
                Html.a [
                    prop.href "/"
                    prop.children [
                        Html.h1 [
                            prop.className "title "
                            prop.style [
                                style.custom ("fontSize", "clamp(2.5rem, 12vw, 4rem)")
                                style.display.flex
                                style.flexWrap.wrap
                                style.justifyContent.center
                                style.custom ("gap", length.em 0.5)
                            ]
                            prop.children [
                                Mui.svgIcon [
                                    prop.className "vivus-start"
                                    svgIcon.component' animatedLogo
                                    prop.style [
                                        style.fontSize (length.em 1)
                                    ]
                                ]
                                Html.span siteName
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let footer =
    Footer.footer [ CustomClass "app-footer" ] [
        Mui.container [
            container.maxWidth.md
            container.children [
                Html.div [
                    Html.div [
                        Html.p [
                            str "Contact: "
                            Mui.link [
                                link.color.initial
                                prop.href (sprintf "mailto:%s" contactEmail)
                                prop.text contactEmail
                            ]
                        ]
                        Html.p [
                            str "Source code: "
                            Mui.link [
                                link.color.initial
                                prop.href (sprintf "https://github.com/%s" githubRepo)
                                prop.children [
                                    gitHubIcon [ prop.style [ style.fontSize (length.em 1) ] ]
                                    str (" " + githubRepo)
                                ]
                            ]
                        ]
                        Html.p "If you find any bugs, please open an issue on GitHub"
                    ]
                    Html.div [
                        Mui.svgIcon [
                            svgIcon.component' logo
                            prop.style [
                                style.fontSize (length.rem 5)
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let createTheme isDark = [
    if isDark then theme.palette.type'.dark else theme.palette.type'.light
    theme.palette.background.default' <| if isDark then "#17181c" else "white"
    theme.palette.background.paper <| if isDark then "#353535" else "#fff"
    unbox<IThemeProp> ("palette.background.level2", if isDark then "#2a2a2a" else "#f5f5f5")

    let headingFamily = "'Nunito', 'Segoe UI', sans-serif"
    theme.typography.h1.fontFamily headingFamily
    theme.typography.h2.fontFamily headingFamily
    theme.typography.h3.fontFamily headingFamily
    theme.typography.h4.fontFamily headingFamily
    theme.typography.subtitle1.fontFamily headingFamily

    //everything else
    theme.typography.fontFamily "'Roboto', 'Segoe UI', sans-serif"

    theme.typography.body2.fontSize (length.rem 1)
]

let darkTheme = Styles.createMuiTheme (createTheme true)
let lightTheme = Styles.createMuiTheme (createTheme false)

let themedApp' = React.functionComponent("themed-app", fun (props: {| children: ReactElement list |}) ->
    let theme = if Hooks.useMediaQuery("@media (prefers-color-scheme: dark)") then darkTheme else lightTheme
    Mui.themeProvider [
        themeProvider.theme theme
        themeProvider.children props.children
    ])

let themedApp children = themedApp' {| children = children |}

let isNormalCheckfaceValues state =
    match (state.VidValues) with
    | None
    | Some (CheckfaceValue _, CheckfaceValue _) -> true
    | _ -> false


let render (state:State) (dispatch: Msg -> unit) =
    themedApp [
        Mui.cssBaseline [ ]
        header
        MorphForm.renderContent state dispatch
        MorphForm.renderEncodeImageDialog state dispatch
        if isNormalCheckfaceValues state then
            viewShareContent (getShareState state) (ShareMsg >> dispatch)
            Explain.view ()
        footer
    ]

let inline helmet props = createElement (Fable.Core.JsInterop.import "Helmet" "react-helmet") props

let meta (property:string) content =
    Html.meta [
        prop.custom ("property", property)
        prop.custom ("content", content)
    ]

let viewHead state =
    let canonicalUrl = canonicalUrl state

    helmet [ 
        prop.children [
            Html.title (pageTitle state.VidValues)
            Html.meta [
                prop.custom ("property", "description")
                prop.custom ("name", "description")
                prop.custom ("content", pageDescription None)
            ]
            meta "og:title" (pageTitle state.VidValues)
            meta "og:description" (pageDescription None)
            meta "og:site_name" siteName

            Html.link [ prop.rel.canonical; prop.href canonicalUrl ]
            meta "og:url" canonicalUrl

            match state.VidValues with
            | None -> // just use left value if no video
                meta "og:image" (imgSrc ogImgDim false state.LeftValue)
                meta "og:image:alt" (imgAlt state.LeftValue)
                meta "og:image:width" ogImgDim
                meta "og:image:height" ogImgDim
                meta "og:image:type" "image/jpeg"
                meta "og:type" "website"

            | Some vidValues ->
                let videoSrc = vidSrc ogVideoDim vidValues
                let linkprevSrc = linkpreviewSrc linkpreviewWidth vidValues
                let linkprevAlt = linkpreviewSrc linkpreviewWidth vidValues
                
                meta "og:image" linkprevSrc
                meta "og:image:alt" linkprevAlt
                meta "og:image:width" linkpreviewWidth
                meta "og:image:height" linkpreviewHeight
                meta "og:image:type" "image/jpeg"

                meta "og:type" "video.other"
                meta "og:video" videoSrc
                meta "og:video:secure_url" videoSrc
                meta "og:video:type" "video/mp4"
                meta "og:video:width" ogVideoDim
                meta "og:video:height" ogVideoDim
                meta "twitter:card" "player"
                meta "twitter:player" (videoSrc + "&embed_html=true")
                meta "twitter:player:width" ogVideoDim
                meta "twitter:player:height" ogVideoDim
        ]
    ]


let view state dispatch =
    React.router [
        router.pathMode
        router.onUrlChanged (getCurrentPath >> parseSegments >> UrlChanged >> dispatch)
        router.children [
            viewHead state
            render state dispatch
        ]
    ]