@echo off

cd /d "%~dp0"

docker swarm init 2>nul

echo [1/4] Building Docker images...
echo. 

echo Building Frontend... 
docker build -t frontendservice:latest ../EMS-app-frontend
if %errorlevel% neq 0 (
    echo ERROR: Failed to build Frontend! 
    pause
    exit /b 1
)

REM Build from parent directory with -f to specify Dockerfile
echo Building Auth Service... 
docker build -f auth-service/Dockerfile -t authservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build Auth Service!
    pause
    exit /b 1
)

echo Building User Service... 
docker build -f user-service/Dockerfile -t userservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build User Service!
    pause
    exit /b 1
)

echo Building Device Service... 
docker build -f device-service/Dockerfile -t deviceservice:latest .
if %errorlevel% neq 0 (
    echo ERROR:  Failed to build Device Service!
    pause
    exit /b 1
)

echo Building Simulator Service... 
docker build -f simulator-service/Dockerfile -t simulatorservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build Simulator Service!
    pause
    exit /b 1
)

echo Building Load Balancer Service...
docker build -f load-balancer-service/Dockerfile -t loadbalancerservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build Load Balancer Service! 
    pause
    exit /b 1
)

echo Building Monitoring Service...
docker build -f monitoring-service/Dockerfile -t monitoringservice:latest .
if %errorlevel% neq 0 (
    echo ERROR:  Failed to build Monitoring Service! 
    pause
    exit /b 1
)

echo Building Customer Support Service...
docker build -f customer-support-service/Dockerfile -t customersupportservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build Customer Support Service! 
    pause
    exit /b 1
)

echo Building WebSocket Service...
docker build -f websocket-service/Dockerfile -t websocketservice:latest .
if %errorlevel% neq 0 (
    echo ERROR: Failed to build WebSocket Service!
    pause
    exit /b 1
)

echo. 
echo All images built successfully!
echo.



echo [3/5] Checking if stack already exists...
docker stack ls | findstr "ems" >nul 2>&1
if %errorlevel% equ 0 (
    echo Stack 'ems' already exists.  Removing it first...
    docker stack rm ems
    echo Waiting for cleanup...
    timeout /t 15 /nobreak >nul
)
echo.

echo [4/5] Deploying stack to Swarm...
docker stack deploy -c docker-compose.yml ems
if %errorlevel% neq 0 (
    echo ERROR: Failed to deploy stack! 
    pause
    exit /b 1
)
echo.

echo [5/5] Waiting for services to start...
timeout /t 10 /nobreak >nul
echo. 

echo ====================================
echo Deployment Complete!
echo ====================================
echo.
echo Services Status:
docker stack services ems
echo. 
echo ====================================
echo Access Points:
echo ====================================
echo Traefik Dashboard:     http://localhost:8080
echo RabbitMQ Management:  http://localhost:15672 (admin/admin123)
echo Frontend:             http://localhost:5173
echo SQL Server:           localhost:1433 (sa/Sqlserver15!)
echo ====================================
echo. 
echo Use these commands: 
echo   status. bat          - Check service status
echo   logs.bat            - View service logs
echo   stop.bat            - Stop all services
echo   restart.bat         - Restart services
echo ====================================
pause