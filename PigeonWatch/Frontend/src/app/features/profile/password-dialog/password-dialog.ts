import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormField, FormRoot, form, minLength, required, validate } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ReLoginError, SessionService } from '../../../core/auth/session.service';
import { ConfigurationService } from '../../../core/configuration/configuration.service';
import { ResourceService } from '../../../core/resources/resource.service';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import {
  Alert,
  FieldErrors,
  FormSubmitState,
  SubmitButton,
  provideFormFieldDefaults,
} from '../../../shared/ui';
import {
  ServerFieldErrors,
  confirmPasswordValidator,
  fallbackPasswordRules,
  passwordClassValidator,
  serverErrorFor,
} from '../../../shared/validation/account-validation';
import {
  AuthFormError,
  PasswordChangeField,
  authErrorMessage,
  passwordChangeFailure,
} from '../../../shared/auth/auth-errors';
import { LoginNavigationState } from '../../../shared/auth/login-navigation-state';

interface PasswordModel {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

const emptyModel: PasswordModel = { currentPassword: '', newPassword: '', confirmPassword: '' };

@Component({
  providers: [provideFormFieldDefaults()],
  selector: 'app-password-dialog',
  imports: [
    FormField,
    FormRoot,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    TranslatePipe,
    Alert,
    FieldErrors,
    SubmitButton,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './password-dialog.scss',
  templateUrl: './password-dialog.html',
})
export class PasswordDialog {
  private readonly session = inject(SessionService);
  private readonly submitState = inject(FormSubmitState);
  private readonly configuration = inject(ConfigurationService);
  private readonly router = inject(Router);
  private readonly resources = inject(ResourceService);
  private readonly dialogRef = inject<MatDialogRef<PasswordDialog, boolean>>(MatDialogRef);
  private readonly model = signal<PasswordModel>({ ...emptyModel });
  private readonly serverErrors = signal<ServerFieldErrors<PasswordChangeField>>({});
  private readonly formError = signal<AuthFormError | null>(null);

  protected readonly passwordRules = computed(
    () => this.configuration.clientConfig()?.passwordRules ?? fallbackPasswordRules,
  );
  protected readonly passwordForm = form(this.model, (path) => {
    required(path.currentPassword);
    validate(path.currentPassword, serverErrorFor(this.serverErrors, 'currentPassword'));
    required(path.newPassword);
    minLength(path.newPassword, () => this.passwordRules().minLength);
    validate(
      path.newPassword,
      passwordClassValidator(() => this.passwordRules()),
    );
    validate(path.newPassword, serverErrorFor(this.serverErrors, 'newPassword'));
    required(path.confirmPassword);
    validate(path.confirmPassword, confirmPasswordValidator(path.newPassword));
  });
  protected readonly busy = signal(false);
  protected readonly formErrorText = computed(() => {
    const error = this.formError();

    return error ? authErrorMessage(this.resources, error) : null;
  });
  protected readonly currentPasswordMessages = computed(() => ({
    required: this.resources.translate('auth.validation.required'),
    wrongCurrentPassword: this.resources.translate('profile.password.errors.wrongCurrent'),
  }));
  protected readonly newPasswordMessages = computed(() => ({
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

    if (this.busy() || this.passwordForm().invalid()) return;

    const passwords = this.model();
    const email = this.session.currentUser()?.email ?? '';
    this.busy.set(true);
    this.dialogRef.disableClose = true;
    this.formError.set(null);
    this.session
      .changePassword(passwords.currentPassword, passwords.newPassword)
      .pipe(
        finalize(() => {
          this.busy.set(false);
          this.dialogRef.disableClose = false;
        }),
      )
      .subscribe({
        next: () => this.dialogRef.close(true),
        error: (error: unknown) => this.handleFailure(error, passwords, email),
      });
  }

  private handleFailure(error: unknown, passwords: PasswordModel, email: string): void {
    if (error instanceof ReLoginError) {
      this.dialogRef.close(false);
      this.navigateToLogin(email);

      return;
    }

    const failure = passwordChangeFailure(error);
    const serverErrors: ServerFieldErrors<PasswordChangeField> = {};

    for (const field of Object.keys(failure.fieldErrors) as PasswordChangeField[]) {
      const kind = failure.fieldErrors[field];

      if (kind) serverErrors[field] = { kind, value: passwords[field] };
    }

    this.serverErrors.set(serverErrors);
    this.formError.set(failure.formError);
  }

  private navigateToLogin(email: string): void {
    const state: LoginNavigationState = {
      email,
      notice: this.resources.translate('profile.password.notices.reloginRequired'),
    };

    void this.router.navigate(['/login'], { state });
  }
}
