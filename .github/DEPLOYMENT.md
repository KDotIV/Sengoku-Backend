# Alexandria deployments

Both workflows build their configured branches, but deploy only `master`, which is
the branch currently trusted by Azure OIDC. Manual runs are supported; select
`master` to deploy. Deployments to each app are serialized.

Both workflows read the subscription from the `AZURE_SUBSCRIPTION_ID` repository secret.
Both workflows use `AZURE_TENANT_ID`. The QA app uses `AZURE_CLIENT_ID`.
The Function uses `AZURE_FUNCTIONAPP_CLIENT_ID` when set, otherwise `AZURE_CLIENT_ID`.

## Required Azure permission (one-time administrator operation)

The existing `sengoku-gh-deploy` identity (client ID
`1e2199d3-ee38-4e79-8012-34c9b1d65ebc`, principal object ID
`1c9734d1-1c96-49ca-9d2e-1102559166b1`) can deploy the QA app, but does not
currently have a deployment role on the Function App. If `AZURE_CLIENT_ID`
selects this identity, an administrator must run:

```powershell
# First select the intended subscription in your local Azure CLI session.
$deploymentSubscriptionId = az account show --query id --output tsv
az role assignment create --assignee-object-id 1c9734d1-1c96-49ca-9d2e-1102559166b1 --assignee-principal-type ServicePrincipal --role "Website Contributor" --scope "/subscriptions/$deploymentSubscriptionId/resourceGroups/Alexandria/providers/Microsoft.Web/sites/EventTournamentScheduler"
```

Alternatively, set `AZURE_FUNCTIONAPP_CLIENT_ID` to a dedicated identity with
Website Contributor on that Function App and a federated credential for issuer
`https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`,
and subject `repo:KDotIV/Sengoku-Backend:ref:refs/heads/master`.
The workflow cannot grant itself missing permissions. This repository change
does not execute the administrator operation above.

## Deployment packages

The Function artifact includes hidden files so `.azurefunctions` survives transfer
between jobs. The downloaded package and Function access are checked before deployment.

The QA artifact contains only the published API at its root, with separately
published worker files under `App_Data/jobs/continuous/SengokuProvider.Worker-1`,
`SengokuProvider.Worker-2`, and `SengokuProvider.Worker-3`. Each gets an explicit
`run.cmd`. This preserves the three existing job names and updates all three to
the same worker version. Each instance runs all hosted services registered by
the worker program. Always On is already enabled in Azure.

After deployment, verify the Function is indexed and all three WebJobs restart
successfully. A successful upload does not verify application startup or its
database and Service Bus connections.
