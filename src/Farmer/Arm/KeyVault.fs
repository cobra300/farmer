[<AutoOpen>]
module Farmer.Arm.KeyVault

open Farmer
open Farmer.KeyVault
open System

let secrets = ResourceType ("Microsoft.KeyVault/vaults/secrets", "2019-09-01")
let accessPolicies = ResourceType ("Microsoft.KeyVault/vaults/accessPolicies", "2019-09-01")
let vaults = ResourceType ("Microsoft.KeyVault/vaults", "2019-09-01")
let keys = ResourceType ("Microsfot.keyVault/vaults/keys", "2019-09-01")

module Vaults =
    type Secret =
        { Name : ResourceName
          Location : Location
          Value : SecretValue
          ContentType : string option
          Enabled : bool option
          ActivationDate : DateTime option
          ExpirationDate : DateTime option
          Dependencies : ResourceId Set
          Tags: Map<string,string> }
        static member ``1970`` = DateTime(1970,1,1,0,0,0)
        static member TotalSecondsSince1970 (d:DateTime) = (d.Subtract Secret.``1970``).TotalSeconds |> int
        interface IParameters with
            member this.SecureParameters =
                match this with
                | { Value = ParameterSecret secureParameter } -> [ secureParameter ]
                | _ -> []
        interface IArmResource with
            member this.ResourceId = secrets.resourceId this.Name
            member this.JsonModel =
                {| secrets.Create(this.Name, this.Location, this.Dependencies, this.Tags) with
                    properties =
                        {| value = this.Value.Value
                           contentType = this.ContentType |> Option.toObj
                           attributes =
                            {| enabled = this.Enabled |> Option.toNullable
                               nbf = this.ActivationDate |> Option.map Secret.TotalSecondsSince1970 |> Option.toNullable
                               exp = this.ExpirationDate |> Option.map Secret.TotalSecondsSince1970 |> Option.toNullable
                            |}
                        |}
                |} :> _

type CreateMode = Recover | Default
type Vault =
    { Name : ResourceName
      Location : Location
      TenantId : string
      Sku : KeyVault.Sku
      Uri : Uri option
      Deployment : FeatureFlag option
      DiskEncryption : FeatureFlag option
      RbacAuthorization : FeatureFlag option
      TemplateDeployment : FeatureFlag option
      SoftDelete : SoftDeletionMode option
      CreateMode : CreateMode option
      AccessPolicies :
        {| ObjectId : ArmExpression
           ApplicationId : Guid option
           Permissions :
            {| Keys : Key Set
               Secrets : Secret Set
               Certificates : Certificate Set
               Storage : Storage Set |}
        |} list
      DefaultAction : DefaultAction option
      Bypass: Bypass option
      IpRules : string list
      VnetRules : string list
      Tags: Map<string,string>  }
      member this.PurgeProtection =
        match this.SoftDelete with
        | None
        | Some SoftDeletionOnly ->
            None
        | Some SoftDeleteWithPurgeProtection ->
            Some true
      member private _.ToStringArray s = s |> Set.map(fun s -> s.ToString().ToLower()) |> Set.toArray
      member this.Dependencies =
        this.AccessPolicies
        |> List.choose(fun r -> r.ObjectId.Owner)
        |> List.distinct
    interface IArmResource with
        member this.ResourceId = vaults.resourceId this.Name
        member this.JsonModel =
            {| vaults.Create(this.Name, this.Location, this.Dependencies, this.Tags) with
                properties =
                    {| tenantId = this.TenantId
                       sku = {| name = this.Sku.ArmValue; family = "A" |}
                       enabledForDeployment = this.Deployment |> Option.map(fun f -> f.AsBoolean) |> Option.toNullable
                       enabledForDiskEncryption = this.DiskEncryption |> Option.map(fun f -> f.AsBoolean) |> Option.toNullable
                       enabledForTemplateDeployment = this.TemplateDeployment |> Option.map(fun f -> f.AsBoolean) |> Option.toNullable
                       enableRbacAuthorization = this.RbacAuthorization |> Option.map(fun f -> f.AsBoolean) |> Option.toNullable
                       enableSoftDelete =
                        match this.SoftDelete with
                        | None ->
                            Nullable()
                        | Some SoftDeleteWithPurgeProtection
                        | Some SoftDeletionOnly ->
                            Nullable true
                       createMode = this.CreateMode |> Option.map(fun m -> m.ToString().ToLower()) |> Option.toObj
                       enablePurgeProtection = this.PurgeProtection |> Option.toNullable
                       vaultUri = this.Uri |> Option.map string |> Option.toObj
                       accessPolicies = [|
                        for policy in this.AccessPolicies do
                            {| objectId = ArmExpression.Eval policy.ObjectId
                               tenantId = this.TenantId
                               applicationId = policy.ApplicationId |> Option.map string |> Option.toObj
                               permissions =
                                {| keys = this.ToStringArray policy.Permissions.Keys
                                   storage = this.ToStringArray policy.Permissions.Storage
                                   certificates = this.ToStringArray policy.Permissions.Certificates
                                   secrets = this.ToStringArray policy.Permissions.Secrets |}
                            |}
                       |]
                       networkAcls =
                        {| defaultAction = this.DefaultAction  |> Option.map string |> Option.toObj
                           bypass = this.Bypass  |> Option.map string |> Option.toObj
                           ipRules = this.IpRules
                           virtualNetworkRules = this.VnetRules |}
                    |}
            |} :> _

type VaultAddPolicies =
    { KeyVault : LinkedResource
      TenantId : string option
      AccessPolicies :
        {| ObjectId : ArmExpression
           ApplicationId : Guid option
           Permissions :
            {| Keys : Key Set
               Secrets : Secret Set
               Certificates : Certificate Set
               Storage : Storage Set |}
        |} list
    }
    member private _.ToStringArray s = s |> Set.map(fun s -> s.ToString().ToLower()) |> Set.toArray
    interface IArmResource with
        member this.ResourceId = accessPolicies.resourceId (this.KeyVault.Name / (ResourceName "add"))
        member this.JsonModel =
            let dependencies =
                match this.KeyVault with
                | Managed kvResId -> [ kvResId ]
                | _ -> []
            {| accessPolicies.Create(this.KeyVault.Name / (ResourceName "add"), dependsOn=dependencies) with
                properties =
                    {| accessPolicies = [|
                        for policy in this.AccessPolicies do
                            {| objectId = ArmExpression.Eval policy.ObjectId
                               tenantId = this.TenantId |> Option.defaultValue "[subscription().tenantId]"
                               applicationId = policy.ApplicationId |> Option.map string |> Option.toObj
                               permissions =
                                {| keys = this.ToStringArray policy.Permissions.Keys
                                   storage = this.ToStringArray policy.Permissions.Storage
                                   certificates = this.ToStringArray policy.Permissions.Certificates
                                   secrets = this.ToStringArray policy.Permissions.Secrets |}
                            |}
                       |]
                    |}
            |} :> _

module Keys =

    type JSONWebKeyCurveName =
      | JSONWebKeyCurveName of string
          member this.ArmValue = match this with JSONWebKeyCurveName name -> name

    [<AutoOpen>]
    module JSONWebKeyCurveNameExtensions =
      type JSONWebKeyCurveName with
        static member P256 = JSONWebKeyCurveName "P256"
        static member P256K = JSONWebKeyCurveName "P256K"
        static member P384 = JSONWebKeyCurveName "P384"
        static member P521 = JSONWebKeyCurveName "P521"

    type JsonWebKeyType =
        | JsonWebKeyType of string
            member this.ArmValue = match this with JsonWebKeyType t -> t

    [<AutoOpen>]
    module JsonWebKeyTypeExtensions =
      type JsonWebKeyType with
        static member EC = JsonWebKeyType "EC"
        static member ECHSM = JsonWebKeyType "EC-HSM"
        static member RSA = JsonWebKeyType "RSA"
        static member RSAHSM = JsonWebKeyType "RSA-HSM"
        static member Oct = JsonWebKeyType "oct"
        static member OctHSM = JsonWebKeyType "oct-HSM"

    type DeletionRecoveryLevel =
        | DeletionRecoveryLevel of string
            member this.ArmValue = match this with DeletionRecoveryLevel level -> level

    [<AutoOpen>]
    module DeletionRecoveryLevelExtensions =
      type DeletionRecoveryLevel with
          static member CustomizedRecoverable = DeletionRecoveryLevel "CustomizedRecoverable"
          static member CustomizedRecoverablePlusProtectedSubscription = DeletionRecoveryLevel "CustomizedRecoverable+ProtectedSubscription"
          static member CustomizedRecoverablePlusPurgeable = DeletionRecoveryLevel "CustomizedRecoverable+Purgeable"
          static member Purgeable = DeletionRecoveryLevel "Purgeable"
          static member Recoverable = DeletionRecoveryLevel "Recoverable"
          static member RecoverablePlusProtectedSubscription = DeletionRecoveryLevel "Recoverable+ProtectedSubscription"
          static member RecoverablePlusPurgeable = DeletionRecoveryLevel "Recoverable+Purgeable"

    type KeyAttributes =
        { Enabled : bool
          Exp : DateTime
          NBF : DateTime}

    type KeyOp =
        | KeyOp of string
            member this.ArmValue = match this with KeyOp op -> op

    [<AutoOpen>]
    module KeyOpExtensions =
      type KeyOp with
          static member Encrypt = KeyOp "encrypt"
          static member Decrypt = KeyOp "decrypt"
          static member WrapKey = KeyOp "wrapKey"
          static member UnwrapKey = KeyOp "unwrapKey"
          static member Sign = KeyOp "sign"
          static member Verify = KeyOp "verify"

    type KeyVaultKey =
        { VaultName : ResourceName
          KeyName : ResourceName
          Attributes : KeyAttributes option
          CRV : JSONWebKeyCurveName option
          KeyOps : KeyOp option
          KeySize : int option
          KTY : JsonWebKeyType option
          Tags : Object option }
        member this.ResourceId = keys.resourceId (this.VaultName / this.KeyName)

        interface IArmResource with
            member this.ResourceId = this.ResourceId
            member this.JsonModel =
              {| name = (this.VaultName / this.KeyName).Value
                 ``type`` = "Microsoft.KeyVault/vaults/keys"
                 apiVersion = "2019-09-01"
                 tags = this.Tags
                 properties =
                   {| attributes =
                        match this.Attributes with
                        | Some a -> {| enabled = a.Enabled; exp = a.Exp; nbf = a.NBF |}
                        | None -> Unchecked.defaultof<_>
                      crv = this.CRV
                      kty = this.KTY
                      key_ops =
                        match this.KeyOps with
                        | Some keyOps -> keyOps.ArmValue
                        | None -> Unchecked.defaultof<_>
                      key_size = this.KeySize |}
              |} :> _