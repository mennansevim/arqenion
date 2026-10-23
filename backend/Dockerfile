# Arqenion backend (.NET 8). Derleme bağlamı depo kökü: model dosyaları (procedure-model/dist) ve simülatör de imaja girer.
#   docker build -f backend/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/Arqenion.Api.csproj backend/
RUN dotnet restore backend/Arqenion.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/Arqenion.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# İçerik kökü /app/backend: "../procedure-model/dist", "../simulator" ve "../assets" yerel düzendeki gibi çözülür.
WORKDIR /app/backend
COPY --from=build /out .
COPY procedure-model/dist /app/procedure-model/dist
COPY simulator /app/simulator
# VR modelleri (/api/assets/manifest, /assets/catalog/<KOD>.glb)
COPY assets /app/assets
ENV Urls=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Arqenion.Api.dll"]
