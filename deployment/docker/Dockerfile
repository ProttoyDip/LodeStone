FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Lodestone.sln", "./"]
COPY ["src/Lodestone.Web/Lodestone.Web.csproj", "src/Lodestone.Web/"]
COPY ["src/Lodestone.Application/Lodestone.Application.csproj", "src/Lodestone.Application/"]
COPY ["src/Lodestone.Domain/Lodestone.Domain.csproj", "src/Lodestone.Domain/"]
COPY ["src/Lodestone.Shared/Lodestone.Shared.csproj", "src/Lodestone.Shared/"]
COPY ["src/Lodestone.Infrastructure/Lodestone.Infrastructure.csproj", "src/Lodestone.Infrastructure/"]
COPY ["src/Lodestone.ML/Lodestone.ML.csproj", "src/Lodestone.ML/"]
COPY ["src/Lodestone.Jobs/Lodestone.Jobs.csproj", "src/Lodestone.Jobs/"]
COPY ["src/Lodestone.Reporting/Lodestone.Reporting.csproj", "src/Lodestone.Reporting/"]

RUN dotnet restore "src/Lodestone.Web/Lodestone.Web.csproj"

COPY . .

RUN dotnet publish "src/Lodestone.Web/Lodestone.Web.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && \
    apt-get install -y curl && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p App_Data/ml keys

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Lodestone.Web.dll"]
