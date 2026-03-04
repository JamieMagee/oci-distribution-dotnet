FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props ./
COPY src/OciDistributionRegistry/OciDistributionRegistry.csproj src/OciDistributionRegistry/
RUN dotnet restore src/OciDistributionRegistry/OciDistributionRegistry.csproj
COPY src/OciDistributionRegistry/ src/OciDistributionRegistry/
RUN dotnet publish src/OciDistributionRegistry/OciDistributionRegistry.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV Storage__Path=/data
VOLUME /data
ENTRYPOINT ["dotnet", "OciDistributionRegistry.dll"]
