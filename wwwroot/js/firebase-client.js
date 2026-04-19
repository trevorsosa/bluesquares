import { initializeApp } from 'https://www.gstatic.com/firebasejs/10.7.1/firebase-app.js';
import {
    getAuth,
    GoogleAuthProvider,
    FacebookAuthProvider,
    signInWithPopup,
    signInWithEmailAndPassword,
    createUserWithEmailAndPassword,
    sendPasswordResetEmail
} from 'https://www.gstatic.com/firebasejs/10.7.1/firebase-auth.js';

export const firebaseConfig = {
    apiKey: "AIzaSyDD0rJH7yjDrO4RbAhSs4wxAsITtLQa3lE",
    authDomain: "bluesquares-b08f1.firebaseapp.com",
    projectId: "bluesquares-b08f1",
    storageBucket: "bluesquares-b08f1.firebasestorage.app",
    messagingSenderId: "1006614878153",
    appId: "1:1006614878153:web:fd8d265433be2cfcc9fa18",
    measurementId: "G-YNW5KHVY5Z"
};

const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);
export const googleProvider = new GoogleAuthProvider();
export const facebookProvider = new FacebookAuthProvider();

googleProvider.setCustomParameters({ prompt: 'select_account' });

export async function loginWithEmail(email, password) {
    return signInWithEmailAndPassword(auth, email, password);
}

export async function signupWithEmail(email, password) {
    return createUserWithEmailAndPassword(auth, email, password);
}

export async function loginWithProvider(provider) {
    return signInWithPopup(auth, provider);
}

export async function sendResetEmail(email) {
    return sendPasswordResetEmail(auth, email);
}

export async function storeSession(user, fallbackEmail = '') {
    const token = await user.getIdToken();
    localStorage.setItem('authToken', token);
    localStorage.setItem('userEmail', user.email || fallbackEmail || '');
    return token;
}

export async function fetchMerchantProfile(token) {
    const response = await fetch('/api/merchants/profile', {
        headers: {
            'Authorization': `Bearer ${token}`
        }
    });

    return response;
}

export async function createOrUpdateMerchantProfile(token, payload) {
    return fetch('/api/merchants/profile', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        },
        body: JSON.stringify(payload)
    });
}

export function currencyFromCountry(country) {
    return country === 'ZA' ? 'ZAR' : country === 'GB' ? 'GBP' : 'EUR';
}

export function rememberPendingSocialProfile({ token, email, name, provider }) {
    sessionStorage.setItem('pendingSocialToken', token);
    sessionStorage.setItem('pendingSocialEmail', email || '');
    sessionStorage.setItem('pendingSocialName', name || '');
    sessionStorage.setItem('pendingSocialProvider', provider || '');
}

export function getPendingSocialProfile() {
    const token = sessionStorage.getItem('pendingSocialToken');
    if (!token) return null;

    return {
        token,
        email: sessionStorage.getItem('pendingSocialEmail') || '',
        name: sessionStorage.getItem('pendingSocialName') || '',
        provider: sessionStorage.getItem('pendingSocialProvider') || ''
    };
}

export function clearPendingSocialProfile() {
    sessionStorage.removeItem('pendingSocialToken');
    sessionStorage.removeItem('pendingSocialEmail');
    sessionStorage.removeItem('pendingSocialName');
    sessionStorage.removeItem('pendingSocialProvider');
}

export function friendlyAuthError(error) {
    switch (error?.code) {
        case 'auth/account-exists-with-different-credential':
            return 'An account already exists with this email using a different sign-in method.';
        case 'auth/popup-closed-by-user':
            return 'The sign-in popup was closed before completing sign-in.';
        case 'auth/cancelled-popup-request':
            return 'Another sign-in popup is already open.';
        case 'auth/invalid-email':
            return 'Please enter a valid email address.';
        case 'auth/user-not-found':
            return 'No account was found with that email address.';
        case 'auth/wrong-password':
        case 'auth/invalid-credential':
            return 'Invalid email or password. Please try again.';
        case 'auth/email-already-in-use':
            return 'This email is already registered. Please log in instead.';
        case 'auth/weak-password':
            return 'Password is too weak. Please use at least 6 characters.';
        case 'auth/operation-not-allowed':
            return 'This sign-in method is not enabled in Firebase yet.';
        case 'auth/missing-email':
            return 'Please enter your email address first.';
        default:
            return 'Something went wrong. Please try again.';
    }
}
