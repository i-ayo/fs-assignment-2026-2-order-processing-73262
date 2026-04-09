FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Domain/Shared.Domain.csproj",       "src/Shared.Domain/"]
COPY ["src/ShippingService/ShippingService.csproj",   "src/ShippingService/"]
RUN dotnet restore "src/ShippingService/ShippingService.csproj"
COPY src/Shared.Domain/    src/Shared.Domain/
COPY src/ShippingService/  src/ShippingService/
WORKDIR "/src/src/ShippingService"
RUN dotnet build "ShippingService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ShippingService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ShippingService.dll"]
