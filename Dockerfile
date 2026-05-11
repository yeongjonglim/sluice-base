# Stage 1: Build the React frontend
FROM node:24-alpine AS frontend-build
WORKDIR /app/src/frontend

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci

# The prebuild gen:api script references ../SluiceBase.Api/openapi.json
COPY src/SluiceBase.Api/openapi.json /app/src/SluiceBase.Api/openapi.json
COPY src/frontend/ ./
RUN npm run build

# Stage 2: Build the .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api-build
WORKDIR /src

# Copy project files first for restore layer caching
COPY Directory.Build.props .
COPY .editorconfig .
COPY src/SluiceBase.Api/SluiceBase.Api.csproj src/SluiceBase.Api/
COPY src/SluiceBase.Core/SluiceBase.Core.csproj src/SluiceBase.Core/
COPY src/ServiceDefaults/ServiceDefaults.csproj src/ServiceDefaults/

RUN dotnet restore src/SluiceBase.Api/SluiceBase.Api.csproj

COPY src/SluiceBase.Api/ src/SluiceBase.Api/
COPY src/SluiceBase.Core/ src/SluiceBase.Core/
COPY src/ServiceDefaults/ src/ServiceDefaults/

RUN dotnet publish src/SluiceBase.Api/SluiceBase.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore

# Stage 3: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

COPY --from=api-build /publish .
COPY --from=frontend-build /app/src/frontend/dist ./wwwroot

EXPOSE 8080
ENTRYPOINT ["dotnet", "SluiceBase.Api.dll"]
