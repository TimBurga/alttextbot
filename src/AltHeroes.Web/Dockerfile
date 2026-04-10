FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy project files first so NuGet restore is cached independently of source changes
COPY Directory.Build.props Directory.Packages.props ./
COPY src/AltHeroes.Core/AltHeroes.Core.csproj                        src/AltHeroes.Core/
COPY src/AltHeroes.ServiceDefaults/AltHeroes.ServiceDefaults.csproj  src/AltHeroes.ServiceDefaults/
COPY src/AltHeroes.Web/AltHeroes.Web.csproj                          src/AltHeroes.Web/
RUN dotnet restore src/AltHeroes.Web/AltHeroes.Web.csproj

COPY . .
RUN dotnet publish src/AltHeroes.Web/AltHeroes.Web.csproj \
    -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AltHeroes.Web.dll"]
