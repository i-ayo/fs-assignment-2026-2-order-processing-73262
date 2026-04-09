FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Domain/Shared.Domain.csproj",         "src/Shared.Domain/"]
COPY ["src/OrderManagement.API/OrderManagement.API.csproj", "src/OrderManagement.API/"]
RUN dotnet restore "src/OrderManagement.API/OrderManagement.API.csproj"
COPY src/Shared.Domain/         src/Shared.Domain/
COPY src/OrderManagement.API/   src/OrderManagement.API/
WORKDIR "/src/src/OrderManagement.API"
RUN dotnet build "OrderManagement.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderManagement.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderManagement.API.dll"]
