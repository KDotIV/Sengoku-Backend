# Sengoku-API

Sengoku-API is an open-source API developed by MERCs. This API provides various functionalities and can be served locally over HTTPS. This README provides setup instructions, configuration details, and other important information to get you started.

## Table of Contents
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Configuration](#configuration)
- [Database Setup](#database-setup)
- [GraphQL Setup](#graphql-setup)
- [Running the API](#running-the-api)
- [Packages](#packages)
- [Contributing](#contributing)

## Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL](https://www.postgresql.org/download/)
- [start.gg Developer Account](https://developer.start.gg/)

## Installation
1. Clone the repository:
```bash
git clone https://github.com/KDotIV/Sengoku-API.git
cd Sengoku-API
```

2. Restore the .NET packages:
```bash
    dotnet restore
```

## Configuration
The `appsettings.json` file contains the necessary configuration settings for the API. Here is an example configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "AlexandriaConnectionString": ""
  },
  "GraphQLSettings": {
    "Endpoint": "https://api.start.gg/gql/alpha",
    "Bearer": ""
  },
  "ServiceBusSettings": {
    "AzureWebJobServiceBus": ""
  }
}
```

## Database Setup
The AlexandriaConnectionString is used to connect to your PostgreSQL database. You need to follow the schema provided in the source code to create the necessary tables.

Set up PostgreSQL and create a database.
Update the AlexandriaConnectionString in appsettings.json with your PostgreSQL connection string.
Run the database migrations or create the tables manually based on the schema in the source code.
## GraphQL Setup
To use the GraphQL functionality, you need to create a developer account on start.gg and obtain an API key.

Sign up or log in to start.gg.
Create an API key.
Update the Bearer token in appsettings.json with your start.gg API key.

## Running the API
Set up the port for SengokuProvider.API.http:

@SengokuProvider.API_HostAddress = https://localhost:5150

GET {{SengokuProvider.API_HostAddress}}/api/core/Pulse
Accept: application/json
Run the API:

```bash
dotnet run --project SengokuProvider.API
```
Access the API at https://localhost:5150.

## Contributing
If you would like to contribute to the development of Sengoku-API, please follow these steps:

1. Fork the repository.
2. Create a new branch (git checkout -b feature-branch).
3. Make your changes and commit them (git commit -m 'Add some feature').
4. Push to the branch (git push origin feature-branch).
5. Create a new Pull Request.
6. Please note that any addition or merge of code to the original source must be approved by the original creators (anyone employed by MERCs).