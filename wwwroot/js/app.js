// Common utilities and Firebase configuration
const API_BASE = '';  // Same origin

// Firebase configuration (UPDATE THIS WITH YOUR CONFIG)
const firebaseConfig = {
    apiKey: "AIzaSyDD0rJH7yjDrO4RbAhSs4wxAsITtLQa3lE",
    authDomain: "bluesquares-b08f1.firebaseapp.com",
    projectId: "bluesquares-b08f1",
    storageBucket: "bluesquares-b08f1.firebasestorage.app",
    messagingSenderId: "1006614878153",
    appId: "1:1006614878153:web:fd8d265433be2cfcc9fa18",
    measurementId: "G-YNW5KHVY5Z"
  };

// Helper function to get auth token
function getAuthToken() {
    return localStorage.getItem('authToken');
}

// Helper function to make authenticated API calls
async function apiCall(endpoint, options = {}) {
    const token = getAuthToken();
    
    const defaultOptions = {
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        }
    };

    const mergedOptions = {
        ...defaultOptions,
        ...options,
        headers: {
            ...defaultOptions.headers,
            ...options.headers
        }
    };

    const response = await fetch(`${API_BASE}${endpoint}`, mergedOptions);
    
    if (response.status === 401) {
        // Unauthorized - redirect to login
        window.location.href = '/login';
        return null;
    }

    return response;
}

// Format currency based on currency code
function formatCurrency(amount, currency) {
    const symbols = {
        'ZAR': 'R',
        'GBP': '£',
        'EUR': '€'
    };

    const locales = {
        'ZAR': 'en-ZA',
        'GBP': 'en-GB',
        'EUR': 'en-IE'
    };

    const symbol = symbols[currency] || currency;
    const locale = locales[currency] || 'en-GB';
    return `${symbol}${Number(amount).toLocaleString(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

async function apiJson(endpoint, options = {}) {
    const response = await apiCall(endpoint, options);
    if (!response) return null;

    const data = await response.json().catch(() => null);
    return { ok: response.ok, status: response.status, data };
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

// Format date
function formatDate(dateString) {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}

// Calculate days overdue
function daysOverdue(dueDate) {
    const due = new Date(dueDate);
    const today = new Date();
    const diffTime = today - due;
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
    return diffDays;
}

// Get status badge class
function getStatusBadgeClass(status) {
    const classes = {
        'Draft': 'bg-secondary',
        'Sent': 'bg-info',
        'Viewed': 'bg-primary',
        'Paid': 'bg-success',
        'Overdue': 'bg-danger',
        'Disputed': 'bg-warning'
    };
    return classes[status] || 'bg-secondary';
}

// Check if user is authenticated
function checkAuth() {
    const token = getAuthToken();
    if (!token) {
        window.location.href = '/login';
        return false;
    }
    return true;
}

// Logout function
function logout() {
    localStorage.removeItem('authToken');
    localStorage.removeItem('userEmail');
    window.location.href = '/';
}

// Show toast notification
function showToast(message, type = 'success') {
    const toastContainer = document.getElementById('toast-container');
    if (!toastContainer) return;

    const toastId = 'toast-' + Date.now();
    const bgClass = type === 'success' ? 'bg-success' : type === 'error' ? 'bg-danger' : 'bg-info';

    const toastHtml = `
        <div id="${toastId}" class="toast align-items-center text-white ${bgClass} border-0" role="alert">
            <div class="d-flex">
                <div class="toast-body">${message}</div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        </div>
    `;

    toastContainer.insertAdjacentHTML('beforeend', toastHtml);
    
    const toastElement = document.getElementById(toastId);
    const toast = new bootstrap.Toast(toastElement);
    toast.show();

    // Remove toast element after it's hidden
    toastElement.addEventListener('hidden.bs.toast', () => {
        toastElement.remove();
    });
}
