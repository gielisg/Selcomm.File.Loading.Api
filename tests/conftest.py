"""
Pytest fixtures for the File Loading API V4 test suite.
"""
import json
import pytest
import requests
from pathlib import Path

# Server config — API runs on Linux production server
SERVER = "10.1.20.55"
FILE_LOADING_PORT = 5140
AUTHENTICATION_PORT = 5001

BASE_URL = f"http://{SERVER}:{FILE_LOADING_PORT}/api/v4/file-loading"
AUTH_URL = f"http://{SERVER}:{AUTHENTICATION_PORT}/api/v4/authentication"

LOGINS_PATH = Path(r"C:\Selcomm.Authentication.Api\tests\Logins\logins.json")


@pytest.fixture(scope="session")
def base_url():
    """Base URL for the File Loading API."""
    return BASE_URL


@pytest.fixture(scope="session")
def jwt_token():
    """
    Authenticate via the Authentication API and return a fresh JWT token.
    Uses credentials from logins.json against the demo3 domain.
    """
    login_url = f"{AUTH_URL}/login"
    payload = {
        "domain": "demo3",
        "username": "operations01",
        "password": "iot@Billing1"
    }
    try:
        response = requests.post(login_url, json=payload, timeout=10)
        response.raise_for_status()
        data = response.json()
        token = data.get("Token") or data.get("token") or data.get("accessToken")
        if not token:
            pytest.skip("Could not extract JWT token from login response")
        return token
    except requests.exceptions.ConnectionError:
        pytest.skip(f"Authentication API is not running on {SERVER}:{AUTHENTICATION_PORT}")
    except requests.exceptions.RequestException as exc:
        pytest.skip(f"Authentication failed: {exc}")


@pytest.fixture(scope="session")
def auth_headers(jwt_token):
    """Authorization headers using JWT bearer token."""
    return {"Authorization": f"Bearer {jwt_token}"}


@pytest.fixture(scope="session")
def api_key_headers():
    """Authorization headers using API key from logins.json."""
    logins = json.loads(LOGINS_PATH.read_text(encoding="utf-8-sig"))
    api_key = logins["demo3"]["apiKeys"][0]["apiKey"]
    return {"X-API-Key": api_key}
