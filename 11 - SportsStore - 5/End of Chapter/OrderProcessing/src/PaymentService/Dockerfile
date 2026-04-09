FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Shared.Domain/Shared.Domain.csproj",     "src/Shared.Domain/"]
COPY ["src/PaymentService/PaymentService.csproj",   "src/PaymentService/"]
RUN dotnet restore "src/PaymentService/PaymentService.csproj"
COPY src/Shared.Domain/   src/Shared.Domain/
COPY src/PaymentService/  src/PaymentService/
WORKDIR "/src/src/PaymentService"
RUN dotnet build "PaymentService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PaymentService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaymentService.dll"]
