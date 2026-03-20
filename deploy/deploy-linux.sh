#!/bin/bash
# Deploy File Loading API to Linux server
# Usage: sudo ./deploy-linux.sh [--no-start]

set -e

# Configuration
INSTALL_DIR="/var/www/api/v4/file_loading_api"
CONFIG_DIR="/etc/selcomm"
LOG_DIR="/var/log/file-loading-api"
SERVICE_NAME="file_loading_api"
OWNER="weblocal:webusers"
PORT=5140
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
APP_DLL="FileLoading.dll"
APP_EXE="FileLoading"

# Parse arguments
NO_START=false
for arg in "$@"; do
    case $arg in
        --no-start)
            NO_START=true
            shift
            ;;
    esac
done

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "Error: This script must be run as root (use sudo)"
    exit 1
fi

echo "============================================"
echo "Deploying File Loading API (port $PORT)"
echo "============================================"

# Stop service if running
echo "Stopping existing service..."
systemctl stop $SERVICE_NAME 2>/dev/null || true

# Create directories
echo "Creating directories..."
mkdir -p $INSTALL_DIR
mkdir -p $CONFIG_DIR
mkdir -p $LOG_DIR
mkdir -p $INSTALL_DIR/logs

# Determine source directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="$SCRIPT_DIR"

# Check for published files
if [ ! -f "$SOURCE_DIR/$APP_DLL" ]; then
    SOURCE_DIR="$SCRIPT_DIR/../publish/linux-x64"
    if [ ! -f "$SOURCE_DIR/$APP_DLL" ]; then
        echo "Error: Cannot find $APP_DLL"
        echo "Searched: $SCRIPT_DIR and $SOURCE_DIR"
        echo "Ensure the application has been published first."
        exit 1
    fi
fi

echo "Source: $SOURCE_DIR"

# Clean install directory (preserve logs and symlinks)
echo "Cleaning install directory..."
find "$INSTALL_DIR" -maxdepth 1 -type f -delete
find "$INSTALL_DIR" -maxdepth 1 -mindepth 1 -type d ! -name "logs" -exec rm -rf {} \;

# Copy application files (exclude deploy scripts and service file)
echo "Installing application files to $INSTALL_DIR..."
find "$SOURCE_DIR" -maxdepth 1 -type f \
    ! -name "deploy-linux.sh" \
    ! -name "file-loading-api.service" \
    ! -name "*.tar.gz" \
    -exec cp {} "$INSTALL_DIR/" \;

# Copy subdirectories (excluding clidriver - uses central /opt/ibm/db2/clidriver/)
find "$SOURCE_DIR" -maxdepth 1 -mindepth 1 -type d -not -name clidriver \
    -exec cp -r {} "$INSTALL_DIR/" \;

# Set executable permissions
chmod +x $INSTALL_DIR/$APP_EXE

# Symlink to central clidriver (DB2 .NET provider resolves relative to app dir)
if [ -f "/opt/ibm/db2/clidriver/lib/libdb2.so" ]; then
    ln -sf /opt/ibm/db2/clidriver "$INSTALL_DIR/clidriver"
    echo "Symlinked clidriver -> /opt/ibm/db2/clidriver"
else
    echo "WARNING: Central clidriver not found at /opt/ibm/db2/clidriver/lib/libdb2.so"
    echo "DB2 connections will fail. Install clidriver first."
fi

# Copy service file
echo "Installing systemd service..."
if [ -f "$SCRIPT_DIR/file-loading-api.service" ]; then
    cp "$SCRIPT_DIR/file-loading-api.service" $SERVICE_FILE
elif [ -f "$SCRIPT_DIR/../deploy/file-loading-api.service" ]; then
    cp "$SCRIPT_DIR/../deploy/file-loading-api.service" $SERVICE_FILE
fi

# Set ownership
echo "Setting permissions..."
chown -R $OWNER $INSTALL_DIR
chown -R $OWNER $LOG_DIR

# Reload systemd
echo "Reloading systemd..."
systemctl daemon-reload

# Enable service
echo "Enabling service..."
systemctl enable $SERVICE_NAME

# Start service unless --no-start
if [ "$NO_START" = false ]; then
    echo "Starting service..."
    systemctl start $SERVICE_NAME

    # Wait for startup
    sleep 3

    # Verify service is running
    if systemctl is-active --quiet $SERVICE_NAME; then
        echo ""
        echo "============================================"
        echo "Deployment successful!"
        echo "============================================"
        systemctl status $SERVICE_NAME --no-pager
    else
        echo ""
        echo "Warning: Service may not have started correctly"
        echo "Check logs: journalctl -u $SERVICE_NAME -f"
        exit 1
    fi

    # Verify port is listening
    echo ""
    echo "Checking port $PORT..."
    if ss -tlnp | grep -q ":$PORT "; then
        echo "Port $PORT is listening"
    else
        echo "Warning: Port $PORT is not listening yet (may still be starting)"
    fi

    # Health check
    echo ""
    echo "Health check..."
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:$PORT/swagger/index.html 2>/dev/null || echo "000")
    if [ "$HTTP_CODE" = "200" ]; then
        echo "Swagger endpoint: OK (HTTP $HTTP_CODE)"
    else
        echo "Swagger endpoint: HTTP $HTTP_CODE (may still be starting)"
    fi
else
    echo ""
    echo "============================================"
    echo "Deployment complete (service not started)"
    echo "============================================"
    echo "Start manually: systemctl start $SERVICE_NAME"
fi

echo ""
echo "Useful commands:"
echo "  Status:  systemctl status $SERVICE_NAME"
echo "  Logs:    journalctl -u $SERVICE_NAME -f"
echo "  Stop:    systemctl stop $SERVICE_NAME"
echo "  Restart: systemctl restart $SERVICE_NAME"
echo ""
echo "Configuration: $CONFIG_DIR/appsettings.shared.json"
echo "Application:   $INSTALL_DIR/"
echo "Logs:          $LOG_DIR/"
