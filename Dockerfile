FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/AuthService/AuthService.csproj src/AuthService/
RUN dotnet restore src/AuthService/AuthService.csproj
COPY src/ src/
RUN dotnet publish src/AuthService/AuthService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Usuario sin privilegios que trae la imagen oficial.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AuthService.dll"]
