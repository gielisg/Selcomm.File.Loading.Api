"""
Pytest fixtures for the File Loading API V4 test suite.
"""
import pytest
import requests

# Service ports
FILE_LOADING_PORT = 5140
AUTHENTICATION_PORT = 5130

BASE_URL = f"http://localhost:{FILE_LOADING_PORT}/api/v4/file-loading"
AUTH_URL = f"http://localhost:{AUTHENTICATION_PORT}/api/v4/authentication"


@pytest.fixture(scope="session")
def base_url():
    """Base URL for the File Loading API."""
    return BASE_URL


@pytest.fixture(scope="session")
def jwt_token():
    """
    Authenticate via the Authentication API and return a JWT token.
    Uses standard test credentials against the demo3 domain.
    """
    login_url = f"{AUTH_URL}/login"
    payload = {
        "domain": "demo3",
        "username": "admin",
        "password": "admin"
    }
    try:
        response = requests.post(login_url, json=payload, timeout=10)
        response.raise_for_status()
        data = response.json()
        # Token may be at top level or nested under a key
        token = data.get("token") or data.get("accessToken") or data.get("access_token")
        if not token:
            pytest.skip("Could not extract JWT token from authentication response")
        return token
    except requests.exceptions.ConnectionError:
        pytest.skip("Authentication API is not running on port 5130")
    except requests.exceptions.RequestException as exc:
        pytest.skip(f"Authentication failed: {exc}")


@pytest.fixture(scope="session")
def auth_headers(jwt_token):
    """Authorization headers using JWT bearer token."""
    return {"Authorization": f"Bearer {jwt_token}"}


@pytest.fixture(scope="session")
def api_key_headers():
    """Authorization headers using API key."""
    return {"X-API-Key": "test-api-key"}
