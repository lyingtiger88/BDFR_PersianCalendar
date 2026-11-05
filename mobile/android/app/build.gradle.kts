plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.bdfr.zurvan"
    compileSdk = 35

    buildFeatures {
        compose = true
        buildConfig = true
    }

    val deskApiBaseUrl = providers.gradleProperty("ZURVAN_DESK_API_BASE_URL")
        .orElse("http://10.0.2.2:5088")
        .get()

    defaultConfig {
        applicationId = "com.bdfr.zurvan"
        val issuerPublicKey = providers.gradleProperty("ZURVAN_LICENSE_PUBLIC_KEY_B64")
            .orElse("").get()
        buildConfigField("String", "LICENSE_PUBLIC_KEY_B64", "\"" + issuerPublicKey + "\"")
        val supabaseUrl = providers.gradleProperty("ZURVAN_SUPABASE_URL").orElse("").get()
        val supabaseKey = providers.gradleProperty("ZURVAN_SUPABASE_PUBLISHABLE_KEY").orElse("").get()
        buildConfigField("String", "SUPABASE_URL", "\"" + supabaseUrl + "\"")
        buildConfigField("String", "SUPABASE_PUBLISHABLE_KEY", "\"" + supabaseKey + "\"")
        minSdk = 26
        targetSdk = 35
        versionCode = 69
        versionName = "0.6.0-beta.10"
        val ciRunNumber = providers.gradleProperty("ZURVAN_CI_RUN_NUMBER").orElse("local").get()
        buildConfigField("String", "CI_RUN_NUMBER", "\"$ciRunNumber\"")
        buildConfigField(
            "String",
            "DESK_API_BASE_URL",
            "\"$deskApiBaseUrl\""
        )
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.01.01")
    implementation(composeBom)
    androidTestImplementation(composeBom)

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.work:work-runtime-ktx:2.10.0")
    implementation("androidx.activity:activity-compose:1.10.0")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.8.7")
    implementation("androidx.compose.material3:material3-window-size-class")
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
    implementation("com.google.zxing:core:3.5.4")
    implementation("org.bouncycastle:bcprov-jdk15to18:1.78.1")
    debugImplementation("androidx.compose.ui:ui-tooling")
}
