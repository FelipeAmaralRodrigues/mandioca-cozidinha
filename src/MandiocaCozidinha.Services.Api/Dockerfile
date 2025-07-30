FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MandiocaCozidinha.Services.Api/MandiocaCozidinha.Services.Api.csproj", "MandiocaCozidinha.Services.Api/"]
RUN dotnet restore "MandiocaCozidinha.Services.Api/MandiocaCozidinha.Services.Api.csproj"
COPY . .
WORKDIR "/src/MandiocaCozidinha.Services.Api"
RUN dotnet build "MandiocaCozidinha.Services.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MandiocaCozidinha.Services.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MandiocaCozidinha.Services.Api.dll"]