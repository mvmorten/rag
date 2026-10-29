# Builds the React UI and the ASP.NET Core API into one image; the API serves the UI.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Rag.slnx ./
COPY src/Rag.Core/Rag.Core.csproj src/Rag.Core/
COPY src/Rag.Api/Rag.Api.csproj src/Rag.Api/
RUN dotnet restore src/Rag.Api/Rag.Api.csproj
COPY src/Rag.Core src/Rag.Core
COPY src/Rag.Api src/Rag.Api
RUN dotnet publish src/Rag.Api/Rag.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Rag.Api.dll"]
