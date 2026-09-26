FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Ws.slnx ./
COPY src/Ws.Core/Ws.Core.csproj src/Ws.Core/
COPY src/Ws.Api/Ws.Api.csproj src/Ws.Api/
RUN dotnet restore src/Ws.Api/Ws.Api.csproj
COPY src/ src/
RUN dotnet publish src/Ws.Api/Ws.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    Ws__DatabasePath=/data/ws.db \
    Ws__LogJson=true
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ws.Api.dll"]
