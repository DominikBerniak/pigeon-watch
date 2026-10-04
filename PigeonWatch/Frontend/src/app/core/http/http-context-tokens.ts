import { HttpContextToken } from '@angular/common/http';

export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

export const AUTH_RETRIED = new HttpContextToken<boolean>(() => false);

export const SKIP_WARMUP = new HttpContextToken<boolean>(() => false);
