// src/contexts/AuthContext.tsx
import React, {
  createContext,
  useContext,
  ReactNode,
  useState,
  useEffect,
  useCallback,
} from 'react';
import { useRouter } from 'next/router';
import { signIn, signOut } from 'next-auth/react';
import { loadStripe } from '@stripe/stripe-js';

interface User {
  id: string;
  userName: string;
  email: string;
  isSubscribed: boolean;
}

type AuthContextType = {
  user: User | null;
  token: string | null;
  isSubscribed: boolean;
  login: (credentials: { username: string; password: string }) => Promise<void>;
  register: (user: {
    username: string;
    email: string;
    password: string;
    confirmPassword: string;
    captchaToken: string;
  }) => Promise<void>;
  logout: () => Promise<void>;
  subscribe: (priceId: string) => Promise<void>;
  loginError: string | null;
  isLoginLoading: boolean;
  isAuthLoading: boolean;
  refreshSubscriptionStatus: () => Promise<void>;
  loginWithProvider: (provider: string) => Promise<void>;
};

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }): JSX.Element {
  // Retrieve the base URL from the environment variable.
  // If not set, default to the hardcoded URL.
  const baseUrl = process.env.NEXT_PUBLIC_BASE_URL || "https://api.local.ritualworks.com/api";

  // Helper function to construct the full endpoint.
  // If baseUrl already ends with '/api', don't add another '/api'.
  const buildEndpoint = (path: string): string => {
    if (baseUrl.endsWith('/api')) {
      return `${baseUrl}/${path}`;
    } else {
      return `${baseUrl}/api/${path}`;
    }
  };

  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [isSubscribed, setIsSubscribed] = useState<boolean>(false);
  const [loginError, setLoginError] = useState<string | null>(null);
  const [isLoginLoading, setIsLoginLoading] = useState<boolean>(false);
  const [isAuthLoading, setIsAuthLoading] = useState<boolean>(true);
  const router = useRouter();

  const refreshSubscriptionStatus = useCallback(async () => {
    if (!user) return;
    try {
      const endpoint = buildEndpoint('Subscription/status');
      console.log('Refreshing subscription status from:', endpoint);
      // Include credentials so cookies are sent.
      const res = await fetch(endpoint, { credentials: 'include' });
      if (res.ok) {
        const data = await res.json();
        setIsSubscribed(data.isSubscribed);
        setUser((currentUser) =>
          currentUser ? { ...currentUser, isSubscribed: data.isSubscribed } : null
        );
      } else {
        console.error('Failed to refresh subscription status:', res.statusText);
      }
    } catch (error) {
      console.error('Error refreshing subscription status:', error);
    }
  }, [user, baseUrl]);

  useEffect(() => {
    const checkAuth = async () => {
      setIsAuthLoading(true);
      try {
        // Use the 'verify-token' endpoint (adjusted to match your API route)
        const endpoint = buildEndpoint('Authentication/verify-token');
        console.log('Checking auth from:', endpoint);
        // Include credentials so the cookie is sent.
        const res = await fetch(endpoint, { credentials: 'include' });
        if (res.ok) {
          const session = await res.json();
          if (session.UserId) {
            // Assume your verify-token endpoint returns UserId, UserName, Email, etc.
            setUser({
              id: session.UserId,
              userName: session.UserName,
              email: session.Email,
              isSubscribed: session.isSubscribed || false,
            });
            await refreshSubscriptionStatus();
          }
        } else if (res.status === 401) {
          // No active session; first-time visitors will fall here.
          console.log("No active session found (unauthorized).");
        } else {
          console.error("Unexpected response while checking auth:", res.status);
        }
      } catch (error) {
        console.error('Error checking authentication:', error);
      } finally {
        setIsAuthLoading(false);
      }
    };

    checkAuth();
  }, [refreshSubscriptionStatus, baseUrl]);

  const login = useCallback(
    async ({ username, password }: { username: string; password: string }) => {
      setIsLoginLoading(true);
      setLoginError(null);
      try {
        const result = await signIn('credentials', {
          username,
          password,
          redirect: false,
          callbackUrl: (router.query.redirect as string) || '/resources',
        });
        if (result?.error) {
          setLoginError(result.error);
        } else {
          // Successful login, NextAuth.js handles the session.
        }
      } catch (error) {
        setLoginError('An unexpected error occurred during login.');
        console.error('Login error:', error);
      } finally {
        setIsLoginLoading(false);
      }
    },
    [router]
  );

  const logout = useCallback(async () => {
    try {
      await signOut({ callbackUrl: '/login', redirect: true });
    } catch (error) {
      console.error('Logout error:', error);
    }
  }, []);

  const subscribe = useCallback(
    async (priceId: string) => {
      try {
        const endpoint = buildEndpoint('Subscription/create-checkout-session');
        console.log('Creating checkout session at:', endpoint);
        const res = await fetch(endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            priceId: priceId,
            redirectPath: router.asPath,
          }),
          credentials: 'include',
        });

        if (res.ok) {
          const { sessionId } = await res.json();
          const stripe = await loadStripe(process.env.NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY || '');
          if (stripe) {
            await stripe.redirectToCheckout({ sessionId });
          } else {
            console.error('Stripe failed to load');
          }
        } else {
          const errorData = await res.json();
          const errorMessage = errorData.message || 'Failed to create checkout session';
          console.error('Subscription error:', errorData);
          alert(errorMessage);
        }
      } catch (error) {
        console.error('Error creating checkout session:', error);
        alert('Failed to start subscription. Please check your connection and try again.');
      }
    },
    [router, baseUrl]
  );

  const register = useCallback(
    async (userData: {
      username: string;
      email: string;
      password: string;
      confirmPassword: string;
      captchaToken: string;
    }) => {
      try {
        const endpoint = buildEndpoint('Authentication/register');
        console.log('Registering user at:', endpoint);
        const res = await fetch(endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(userData),
          credentials: 'include',
        });

        if (res.ok) {
          router.push('/login');
        } else {
          const errorData = await res.json();
          const errorMessage = errorData.message || 'Registration failed. Please try again.';
          console.error('Registration failed:', errorData);
          alert(errorMessage);
        }
      } catch (error) {
        console.error('Registration error:', error);
        alert('An unexpected error occurred during registration.');
      }
    },
    [router, baseUrl]
  );

  const loginWithProvider = async (provider: string) => {
    try {
      const result = await signIn(provider, {
        callbackUrl: '/resources',
      });
      if (result?.error) {
        console.error('Social login error:', result.error);
      }
    } catch (error) {
      console.error('Social login error:', error);
    }
  };

  const value: AuthContextType = {
    user,
    token,
    isSubscribed,
    login,
    register,
    logout,
    subscribe,
    loginError,
    isLoginLoading,
    isAuthLoading,
    refreshSubscriptionStatus,
    loginWithProvider,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
