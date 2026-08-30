# Multi-stage build for Lodestone.Web
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore Lodestone.sln
RUN dotnet publish src/Lodestone.Web/Lodestone.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lodestone.Web.dll"]
