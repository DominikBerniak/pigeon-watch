import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  FormField,
  FormRoot,
  email,
  form,
  maxLength,
  minLength,
  required,
  validate,
} from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { safeReturnUrl } from '../../../core/auth/safe-return-url';
import { AutoLoginError, SessionService } from '../../../core/auth/session.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  Alert,
  FieldErrors,
  FormSubmitState,
  PageCard,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import {
  ServerFieldErrors,
  confirmPasswordValidator,
  displayNameCharacterValidator,
  fallbackDisplayNameRules,
  fallbackPasswordRules,
  forbiddenDisplayNameCharacter,
  passwordClassValidator,
  serverErrorFor,
} from '../../../shared/validation/account-validation';
import {
  AuthFormError,
  RegisterField,
  authErrorMessage,
  registerFailure,
} from '../../../shared/auth/auth-errors';
import { LoginNavigationState } from '../../../shared/auth/login-navigation-state';

interface RegisterModel {
  email: string;
  displayName: string;
  password: string;
  confirmPassword: string;
}

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-register-page',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    RouterLink,
    TranslatePipe,
    Alert,
    FieldErrors,
    PageCard,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register-page.html',
})
export class RegisterPage {
  private readonly session = inject(SessionService);
  private readonly submitState = inject(FormSubmitState);
  private readonly configuration = inject(ConfigurationService);
  private readonly router = inject(Router);
  private readonly resources = inject(ResourceService);
  private readonly returnUrl = inject(ActivatedRoute).snapshot.queryParamMap.get('returnUrl');
  private readonly model = signal<RegisterModel>({
    email: '',
    displayName: '',
    password: '',
    confirmPassword: '',
  });
  private readonly serverErrors = signal<ServerFieldErrors<RegisterField>>({});
  private readonly formError = signal<AuthFormError | null>(null);

  protected readonly passwordRules = computed(
    () => this.configuration.clientConfig()?.passwordRules ?? fallbackPasswordRules,
  );
  protected readonly displayNameRules = computed(
    () => this.configuration.clientConfig()?.displayNameRules ?? fallbackDisplayNameRules,
  );
  protected readonly registerForm = form(this.model, (path) => {
    required(path.email);
    email(path.email);
    validate(path.email, serverErrorFor(this.serverErrors, 'email'));
    required(path.displayName);
    minLength(path.displayName, () => this.displayNameRules().minLength);
    maxLength(path.displayName, () => this.displayNameRules().maxLength);
    validate(path.displayName, displayNameCharacterValidator());
    validate(path.displayName, serverErrorFor(this.serverErrors, 'displayName'));
    required(path.password);
    minLength(path.password, () => this.passwordRules().minLength);
    validate(
      path.password,
      passwordClassValidator(() => this.passwordRules()),
    );
    validate(path.password, serverErrorFor(this.serverErrors, 'password'));
    required(path.confirmPassword);
    validate(path.confirmPassword, confirmPasswordValidator(path.password));
  });
  protected readonly busy = signal(false);
  protected readonly formErrorText = computed(() => {
    const error = this.formError();

    return error ? authErrorMessage(this.resources, error) : null;
  });
  protected readonly emailMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    email: this.resources.translate('auth.validation.email'),
    invalidEmail: this.resources.translate('auth.errors.invalidEmail'),
  }));
  protected readonly displayNameMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    minLength: this.resources.translate(
      'auth.validation.displayNameMinLength',
      this.displayNameRules().minLength,
    ),
    maxLength: this.resources.translate(
      'auth.validation.displayNameMaxLength',
      this.displayNameRules().maxLength,
    ),
    invalidCharacter: this.resources.translate(
      'auth.validation.displayNameInvalidCharacter',
      forbiddenDisplayNameCharacter,
    ),
    duplicateDisplayName: this.resources.translate('auth.errors.duplicateDisplayName'),
    invalidDisplayName: this.resources.translate('auth.errors.invalidDisplayName'),
  }));
  protected readonly passwordMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    minLength: this.resources.translate(
      'auth.validation.passwordMinLength',
      this.passwordRules().minLength,
    ),
    requireDigit: this.resources.translate('auth.validation.passwordRequiresDigit'),
    requireLowercase: this.resources.translate('auth.validation.passwordRequiresLowercase'),
    requireUppercase: this.resources.translate('auth.validation.passwordRequiresUppercase'),
    requireNonAlphanumeric: this.resources.translate(
      'auth.validation.passwordRequiresNonAlphanumeric',
    ),
    invalidPassword: this.resources.translate('auth.errors.invalidPassword'),
  }));
  protected readonly confirmPasswordMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    mismatch: this.resources.translate('auth.validation.passwordMismatch'),
  }));

  protected submit(): void {
    this.submitState.markSubmitted();

    if (this.busy() || this.registerForm().invalid()) return;

    const account = this.model();
    this.busy.set(true);
    this.formError.set(null);
    this.session
      .register({
        email: account.email,
        displayName: account.displayName,
        password: account.password,
      })
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => void this.router.navigateByUrl(safeReturnUrl(this.returnUrl)),
        error: (error: unknown) => this.handleFailure(error, account),
      });
  }

  private handleFailure(error: unknown, account: RegisterModel): void {
    if (error instanceof AutoLoginError) {
      this.navigateToLogin(account.email);

      return;
    }

    const failure = registerFailure(error);
    const serverErrors: ServerFieldErrors<RegisterField> = {};

    for (const field of Object.keys(failure.fieldErrors) as RegisterField[]) {
      const kind = failure.fieldErrors[field];

      if (kind) serverErrors[field] = { kind, value: account[field] };
    }

    this.serverErrors.set(serverErrors);
    this.formError.set(failure.formError);
  }

  private navigateToLogin(email: string): void {
    const state: LoginNavigationState = {
      email,
      notice: this.resources.translate('auth.notices.accountCreated'),
    };
    const queryParams = this.returnUrl ? { returnUrl: this.returnUrl } : {};

    void this.router.navigate(['/login'], { queryParams, state });
  }
}
