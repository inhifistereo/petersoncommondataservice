# Deployment Instructions

To deploy this project, you need to set the following environment variables in the GitHub Secrets:

- `TODOIST_API_KEY=`: Your ToDoist API Key
- `TODOIST_PROJECT_ID=`: The ToDoist Project you'd like to filter on. Note: You can update ToDoist Models and Controllers so there is no filter. This is specific for my use case. 
- `AZURE_AD_TENANT_ID`: Your Azure AD Tenant ID.
- `AZURE_AD_CLIENT_ID`: Your Azure AD Client ID.
- `AZURE_AD_CLIENT_SECRET`: Your Azure AD Client Secret.

## Steps to Add Secrets

1. Go to your GitHub repository.
2. Click on "Settings" > "Secrets and variables" > "Actions".
3. Click "New repository secret" and add each of the above environment variables.

## GitHub Actions Workflow

Here is an example of a GitHub Actions workflow file that sets up these environment variables:

```yaml
name: Deploy to Azure

on:
  push:
    branches:
      - main

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v2

      - name: Set up .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '6.0.x'

      - name: Build
        run: dotnet build --configuration Release

      - name: Publish
        run: dotnet publish --configuration Release --output ./publish

      - name: Deploy to Azure
        env:
          TODOIST_API_KEY=: ${{ secrets.TODOIST_API_KEY }}
          TODOIST_PROJECT_ID=: ${{ secrets.TODOIST_PROJECT_ID }}
          AZURE_AD_TENANT_ID: ${{ secrets.AZURE_AD_TENANT_ID }}
          AZURE_AD_CLIENT_ID: ${{ secrets.AZURE_AD_CLIENT_ID }}
          AZURE_AD_CLIENT_SECRET: ${{ secrets.AZURE_AD_CLIENT_SECRET }}
        run: |
          # Add deployment commands here, e.g., az webapp deploy or docker commands