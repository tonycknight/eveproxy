namespace eveproxy.common.tests.unit.Utils

open System
open eveproxy
open eveproxy.common.tests.unit
open FsCheck.Xunit

module ValidatorsTests =
    
    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``mustbe - on validation fail returns error``(value: string) =
        let errMsg = "error"
        let r = value |> Validators.mustBe (fun _ -> false) errMsg
        
        r = (Some errMsg)

    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``mustbe - on validation success returns None``(value: string) =
        let errMsg = "error"
        let r = value |> Validators.mustBe (fun _ -> true) errMsg
        
        r = None

    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``isUrl - invalid returns false``(value: string)=
        value |> Validators.isUrl |> not

    [<Property(Arbitrary = [| typeof<UrlString> |], Verbose = true)>]
    let ``isUrl - valid returns true``(value: string)=
        value |> Validators.isUrl
                
    [<Property(Arbitrary = [| typeof<NullEmptyWhitespaceString> |], Verbose = true)>]
    let ``isNonEmptyString - invalid returns false``(value: string)=
        value |> Validators.isNonEmptyString |> not

    [<Property(Arbitrary = [| typeof<AlphaNumericString> |], Verbose = true)>]
    let ``isNonEmptyString - valid returns true``(value: string)=
        value |> Validators.isNonEmptyString

    